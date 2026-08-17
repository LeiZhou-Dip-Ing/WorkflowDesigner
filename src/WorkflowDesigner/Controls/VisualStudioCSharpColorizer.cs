using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace WorkflowCore.WpfDemo.Controls;

/// <summary>Applies the Visual Studio dark C# palette used by the script editor.</summary>
public sealed class VisualStudioCSharpColorizer : DocumentColorizingTransformer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
        "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "record", "ref", "return",
        "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct",
        "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
        "unsafe", "ushort", "using", "var", "virtual", "void", "volatile", "while",
        "add", "and", "ascending", "async", "await", "by", "descending", "dynamic", "equals",
        "file", "from", "get", "global", "group", "init", "into", "join", "let", "managed",
        "nameof", "nint", "not", "notnull", "nuint", "on", "or", "orderby", "partial", "remove",
        "required", "scoped", "select", "set", "unmanaged", "value", "when", "where", "with", "yield"
    };

    private static readonly HashSet<string> TypeLikeKeywords = new(StringComparer.Ordinal)
    {
        "DateTime", "DateTimeOffset", "Guid", "Regex", "Math", "CultureInfo", "Dictionary",
        "IReadOnlyDictionary", "List", "Enumerable", "Activator", "ValueTask", "CancellationToken",
        "IWorkflowSharpScript", "IWorkflowSharpScriptContext", "ScriptInput", "ScriptOutput"
    };

    private static readonly Brush NormalBrush = CreateBrush(212, 212, 212);
    private static readonly Brush KeywordBrush = CreateBrush(86, 156, 214);
    private static readonly Brush StringBrush = CreateBrush(206, 145, 120);
    private static readonly Brush CommentBrush = CreateBrush(106, 153, 85);
    private static readonly Brush NumberBrush = CreateBrush(181, 206, 168);
    private static readonly Brush TypeBrush = CreateBrush(78, 201, 176);
    private static readonly Brush MethodBrush = CreateBrush(220, 220, 170);
    private static readonly Brush PreprocessorBrush = CreateBrush(155, 155, 155);

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        var offset = line.Offset;
        var index = 0;

        while (index < text.Length)
        {
            var character = text[index];

            if (index + 1 < text.Length && character == '/' && text[index + 1] == '/')
            {
                ApplyColor(offset + index, offset + text.Length, CommentBrush);
                break;
            }

            if (character == '"' || (character == '@' && index + 1 < text.Length && text[index + 1] == '"'))
            {
                var start = index;
                index = ReadString(text, index);
                ApplyColor(offset + start, offset + index, StringBrush);
                continue;
            }

            if (character == '\'')
            {
                var start = index++;
                while (index < text.Length)
                {
                    if (text[index] == '\\')
                    {
                        index += 2;
                        continue;
                    }

                    if (text[index++] == '\'')
                    {
                        break;
                    }
                }

                ApplyColor(offset + start, offset + Math.Min(index, text.Length), StringBrush);
                continue;
            }

            if (character == '#')
            {
                ApplyColor(offset + index, offset + text.Length, PreprocessorBrush);
                break;
            }

            if (char.IsDigit(character))
            {
                var start = index++;
                while (index < text.Length
                       && (char.IsLetterOrDigit(text[index]) || text[index] is '.' or '_'))
                {
                    index++;
                }

                ApplyColor(offset + start, offset + index, NumberBrush);
                continue;
            }

            if (IsIdentifierStart(character))
            {
                var start = index++;
                while (index < text.Length && IsIdentifierPart(text[index]))
                {
                    index++;
                }

                var token = text[start..index];
                var brush = GetIdentifierBrush(text, start, index, token);
                ApplyColor(offset + start, offset + index, brush);
                continue;
            }

            index++;
        }
    }

    private static Brush GetIdentifierBrush(string lineText, int start, int end, string token)
    {
        if (Keywords.Contains(token))
        {
            return KeywordBrush;
        }

        if (TypeLikeKeywords.Contains(token) || IsAttributeName(lineText, start, end))
        {
            return TypeBrush;
        }

        if (IsTypeDeclarationName(lineText, start, token))
        {
            return TypeBrush;
        }

        return IsMethodCall(lineText, end) ? MethodBrush : NormalBrush;
    }

    private static bool IsTypeDeclarationName(string lineText, int start, string token)
    {
        if (token.Length == 0 || !char.IsUpper(token[0]))
        {
            return false;
        }

        var prefix = lineText[..start];
        return prefix.Contains(" class ", StringComparison.Ordinal)
               || prefix.Contains(" interface ", StringComparison.Ordinal)
               || prefix.Contains(" struct ", StringComparison.Ordinal)
               || prefix.Contains(" enum ", StringComparison.Ordinal)
               || prefix.Contains(" record ", StringComparison.Ordinal);
    }

    private static bool IsAttributeName(string lineText, int start, int end)
    {
        var openingBracket = lineText.LastIndexOf('[', Math.Max(0, start));
        if (openingBracket < 0)
        {
            return false;
        }

        return lineText.IndexOf(']', end) >= end;
    }

    private static bool IsMethodCall(string lineText, int end)
    {
        var index = end;
        while (index < lineText.Length && char.IsWhiteSpace(lineText[index]))
        {
            index++;
        }

        return index < lineText.Length && lineText[index] == '(';
    }

    private static int ReadString(string text, int index)
    {
        var isVerbatim = text[index] == '@';
        if (isVerbatim)
        {
            index++;
        }

        index++;
        while (index < text.Length)
        {
            if (!isVerbatim && text[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (text[index] == '"')
            {
                index++;
                if (isVerbatim && index < text.Length && text[index] == '"')
                {
                    index++;
                    continue;
                }

                break;
            }

            index++;
        }

        return Math.Min(index, text.Length);
    }

    private static bool IsIdentifierStart(char character)
        => char.IsLetter(character) || character == '_';

    private static bool IsIdentifierPart(char character)
        => char.IsLetterOrDigit(character) || character == '_';

    private void ApplyColor(int startOffset, int endOffset, Brush brush)
    {
        ChangeLinePart(startOffset, endOffset, element =>
        {
            element.TextRunProperties.SetForegroundBrush(brush);
            element.TextRunProperties.SetTypeface(new Typeface(
                element.TextRunProperties.Typeface.FontFamily,
                element.TextRunProperties.Typeface.Style,
                element.TextRunProperties.Typeface.Weight,
                element.TextRunProperties.Typeface.Stretch));
        });
    }

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
