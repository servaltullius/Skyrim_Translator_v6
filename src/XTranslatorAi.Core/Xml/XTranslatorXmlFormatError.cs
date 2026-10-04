using System;
using System.IO;

namespace XTranslatorAi.Core.Xml;

/// <summary>
/// The file is well-formed XML but not an xTranslator export (a fomod ModuleConfig.xml, a hand-edited file). These
/// errors were English and showed as E999 "예상치 못한 오류"; they are still <see cref="InvalidDataException"/>, marked
/// so that the message is shown to the user as it is.
/// </summary>
public static class XTranslatorXmlFormatError
{
    private const string Marker = "XTranslatorXmlFormat";

    public static InvalidDataException Create(string message)
    {
        var ex = new InvalidDataException(message);
        ex.Data[Marker] = true;
        return ex;
    }

    public static bool IsFormatError(Exception ex) => ex is InvalidDataException && ex.Data.Contains(Marker);
}
