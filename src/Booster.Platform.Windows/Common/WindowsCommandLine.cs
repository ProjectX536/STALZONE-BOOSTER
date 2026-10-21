using System.Text;

namespace Booster.Platform.Windows.Common;

// Строка, которую CommandLineToArgvW разберёт обратно в те же аргументы (для runas).
public static class WindowsCommandLine
{
    public static string Join(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return string.Join(' ', arguments.Select(Quote));
    }

    public static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
            return argument;

        var quoted = new StringBuilder(argument.Length + 2).Append('"');
        var backslashes = 0;
        foreach (var ch in argument)
        {
            if (ch == '\\')
            {
                backslashes++;
                continue;
            }

            // Обратные косые перед кавычкой удваиваются, и сама кавычка экранируется; перед другими символами они буквальны.
            quoted.Append('\\', ch == '"' ? backslashes * 2 + 1 : backslashes).Append(ch);
            backslashes = 0;
        }

        // Перед закрывающей кавычкой обратные косые тоже удваиваются, иначе последняя экранирует кавычку.
        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
