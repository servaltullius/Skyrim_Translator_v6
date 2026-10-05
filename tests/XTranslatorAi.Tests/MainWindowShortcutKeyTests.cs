using System.Windows.Input;
using XTranslatorAi.App;

namespace XTranslatorAi.Tests;

/// <summary>
/// While a Korean syllable is still being composed, WPF reports a key press as ImeProcessed; the review shortcuts
/// matched only the plain key, so Ctrl+S or Ctrl+Enter right after typing a Korean word did nothing.
/// </summary>
public sealed class MainWindowShortcutKeyTests
{
    [Theory]
    [InlineData(Key.Enter, Key.None, Key.None, Key.Enter)]
    [InlineData(Key.System, Key.F8, Key.None, Key.F8)]
    [InlineData(Key.ImeProcessed, Key.None, Key.Enter, Key.Enter)]
    [InlineData(Key.ImeProcessed, Key.None, Key.S, Key.S)]
    public void ShortcutKey_UsesTheKeyBehindTheImeAndSystemKeys(Key key, Key systemKey, Key imeKey, Key expected)
        => Assert.Equal(expected, MainWindow.ShortcutKey(key, systemKey, imeKey));
}
