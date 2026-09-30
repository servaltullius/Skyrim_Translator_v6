using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Diagnostics;

namespace XTranslatorAi.App.Services;

/// <summary>Plugin-operation-only messages. Never forwards arbitrary exception text, filenames or translated text.</summary>
public static class PluginUserFacingErrorClassifier
{
    private const string Table = "(?:Strings|DlStrings|IlStrings)";
    private const string Field = "[A-Z0-9_]{4}:[A-Z0-9_]{4}/[0-9A-F]{8}";

    public static UserFacingError? Classify(Exception exception)
    {
        var message = exception.Message;
        if (message.Length > 4096) return null;
        if (exception is EncoderFallbackException)
            return Error("E457", "현재 출력 인코딩으로 표현할 수 없는 문자가 있습니다. UTF-8 또는 해당 문자를 지원하는 인코딩을 선택해 다시 열어주세요. 기존 번역은 현재 프로젝트에 남아 있습니다.");
        if (exception is FileNotFoundException)
        {
            var missing = Match(message, @"^.+에 필요한 (?<id>STRINGS|DLSTRINGS|ILSTRINGS) 원문 테이블이 없습니다\. Strings 폴더 또는 BSA 경로를 확인하세요\.$");
            if (missing.Success)
                return Error("E451", $"{missing.Groups["id"].Value} 원문 테이블이 없습니다. 원문 언어와 Strings 폴더 또는 원본 BSA가 맞는지 확인하세요.");
        }
        if (exception is InvalidDataException)
        {
            var shared = Match(message, $@"^여러 필드가 같은 (?<table>{Table}) StringID (?<id>[0-9]{{1,10}})를 공유하지만 번역이 다릅니다\.");
            if (shared.Success)
            {
                var table = shared.Groups["table"].Value;
                var id = shared.Groups["id"].Value;
                var rows = Match(message, @" 관련 행: (?<ids>#[0-9]{1,10}(?:, #[0-9]{1,10}){0,11})$");
                var rowHint = rows.Success ? " 관련 행: " + rows.Groups["ids"].Value : "";
                return Error("E453", $"{table} StringID {id}를 공유하는 행의 번역이 다릅니다. Search에 string:{table.ToUpperInvariant()}/{id}를 입력하고 Status를 (All)로 설정한 뒤, 공유 행의 번역을 모두 일치시켜 저장하세요.{rowHint}");
            }
            var decoding = Match(message, $@"^원문 인코딩으로 해석할 수 없습니다: (?<id>{Field}|{Table}:[0-9]{{1,10}}|EDID|MAST)\. 올바른 인코딩을 선택하세요\.$");
            if (decoding.Success)
            {
                var location = decoding.Groups["id"].Value;
                var setting = location is "EDID" or "MAST" ? "메타데이터 인코딩" : "원문 인코딩";
                return Error("E452", $"{location}을 현재 {setting}으로 해석할 수 없습니다. 원본에 맞는 {setting}을 선택하여 다시 열어주세요.");
            }
            var empty = Match(message, $@"^비어 있는 번역을 저장할 수 없습니다: (?<id>{Field})$");
            if (empty.Success)
                return Error("E454", $"{empty.Groups["id"].Value}의 번역이 비어 있습니다. 필수 문자열을 입력하거나 원문으로 되돌린 뒤 저장하세요.");
            var missingId = Match(message, $@"^(?<table>{Table}) 테이블에서 StringID (?<id>[0-9]{{1,10}})를 찾을 수 없습니다\.$");
            if (missingId.Success)
                return Error("E451", $"{missingId.Groups["table"].Value} 테이블에 StringID {missingId.Groups["id"].Value}가 없습니다. 플러그인 버전과 일치하는 원문 Strings/BSA를 선택하세요.");
            if (message.StartsWith("불러온 뒤 원본이 변경되었습니다. 다시 열어주세요: ", StringComparison.Ordinal))
                return SourceChanged();
            if (message == "번역문 안에 NUL 문자를 저장할 수 없습니다.")
                return Error("E459", "번역문에 저장할 수 없는 NUL 문자가 있습니다. 해당 제어 문자를 제거한 뒤 다시 저장하세요.");
            if (message is "TES4 헤더가 있는 Bethesda 플러그인이 아닙니다." or "TES4 HEDR 헤더가 잘못되었습니다." or "ESP/ESM/ESL 파일을 선택하세요.")
                return Error("E458", "Skyrim SE/AE의 ESP/ESM/ESL 형식으로 읽을 수 없습니다. 파일 종류와 원본 파일이 손상되지 않았는지 확인하세요.");
        }
        if (exception is InvalidOperationException && message == "Plugin source changed. Reopen the plugin before exporting.")
            return SourceChanged();
        if (exception is IOException && message == "출력은 아직 존재하지 않는 새 폴더를 선택하세요. 기존 폴더나 원본을 덮어쓰지 않습니다.")
            return Error("E456", "출력에는 아직 존재하지 않는 새 폴더를 지정하세요. 기존 파일과 폴더는 덮어쓰지 않습니다.");
        if (exception is NotSupportedException)
        {
            if (message.StartsWith("지원하지 않는 플러그인 인코딩: ", StringComparison.Ordinal))
                return Error("E458", "지원하지 않는 인코딩입니다. UTF-8, windows-1252(CP1252), ks_c_5601-1987(CP949) 중 원본과 출력에 맞는 값을 선택하세요.");
            if (message == "확인되지 않은 게임 형식입니다." || message.StartsWith("Skyrim 형식 HEDR 1.7/1.71이 아닙니다(", StringComparison.Ordinal))
                return Error("E458", "현재 직접 읽기/저장은 Skyrim SE/AE 형식만 지원합니다. 다른 게임 형식으로 저장할 수 없습니다.");
        }
        return null;
    }

    private static UserFacingError SourceChanged()
        => Error("E455", "불러온 뒤 원본 플러그인 또는 Strings/BSA가 변경되었습니다. 현재 번역은 프로젝트에 보존되어 있습니다. 원본을 다시 열어 확인한 뒤 저장하세요.");
    private static UserFacingError Error(string code, string message) => new(code, message, DetailsInApiLogs: false);
    private static Match Match(string input, string pattern)
        => Regex.Match(input, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
