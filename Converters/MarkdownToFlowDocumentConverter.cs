using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace LolManager.Converters;

public class MarkdownToFlowDocumentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string markdown || string.IsNullOrWhiteSpace(markdown))
        {
            var emptyDocument = new FlowDocument();
            emptyDocument.Blocks.Add(new Paragraph(new Run("История изменений загружается...")));
            return emptyDocument;
        }

        return ParseMarkdownToFlowDocument(markdown);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private FlowDocument ParseMarkdownToFlowDocument(string markdown)
    {
        var document = new FlowDocument();
        var lines = markdown.Split('\n');
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                // Добавляем минимальный отступ вместо полного параграфа
                var emptyParagraph = new Paragraph();
                emptyParagraph.Margin = new Thickness(0, 2, 0, 0);
                emptyParagraph.FontSize = 4;
                document.Blocks.Add(emptyParagraph);
                continue;
            }
            
            // Заголовки (названия релизов)
            if (trimmedLine.StartsWith("# "))
            {
                var paragraph = CreateVersionParagraph(trimmedLine.Substring(2), 16);
                paragraph.Margin = new Thickness(0, 4, 0, 3);
                document.Blocks.Add(paragraph);
            }
            else if (trimmedLine.StartsWith("## "))
            {
                var paragraph = CreateVersionParagraph(trimmedLine.Substring(3), 15);
                paragraph.Margin = new Thickness(0, 3, 0, 2);
                document.Blocks.Add(paragraph);
            }
            else if (trimmedLine.StartsWith("### "))
            {
                var paragraph = new Paragraph(new Run(trimmedLine.Substring(4)));
                paragraph.FontWeight = FontWeights.SemiBold;
                paragraph.FontSize = 13;
                paragraph.Margin = new Thickness(0, 8, 0, 4);
                document.Blocks.Add(paragraph);
            }
            // Списки
            else if (trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("* "))
            {
                var paragraph = new Paragraph(new Run("• " + trimmedLine.Substring(2)));
                paragraph.FontSize = 12;
                paragraph.Margin = new Thickness(16, 1, 0, 1);
                document.Blocks.Add(paragraph);
            }
            // Обычный текст с поддержкой жирного текста и <br>
            else
            {
                var paragraph = new Paragraph();
                ParseInlineFormattingWithBr(trimmedLine, paragraph);
                paragraph.FontSize = 12;
                paragraph.Margin = new Thickness(0, 1, 0, 1);
                document.Blocks.Add(paragraph);
            }
        }
        
        return document;
    }
    
    private Paragraph CreateVersionParagraph(string text, double fontSize)
    {
        var paragraph = new Paragraph();
        paragraph.FontSize = fontSize;
        
        // Ищем паттерн "версия - название" или "версия (Beta) - название"
        var parts = text.Split(new[] { " - " }, 2, StringSplitOptions.None);
        if (parts.Length == 2)
        {
            // Первая часть (версия) - жирная
            var versionRun = new Run(parts[0]);
            versionRun.FontWeight = FontWeights.Bold;
            paragraph.Inlines.Add(versionRun);
            
            // Разделитель
            paragraph.Inlines.Add(new Run(" - "));
            
            // Вторая часть (название) - обычная, чуть крупнее базового
            var nameRun = new Run(parts[1]);
            nameRun.FontSize = fontSize - 1;
            paragraph.Inlines.Add(nameRun);
        }
        else
        {
            // Если нет разделителя " - ", делаем всё жирным
            var run = new Run(text);
            run.FontWeight = FontWeights.Bold;
            paragraph.Inlines.Add(run);
        }
        
        return paragraph;
    }
    
    private void ParseInlineFormatting(string text, Paragraph paragraph)
    {
        var parts = text.Split(new string[] { "**" }, StringSplitOptions.None);
        bool isBold = false;
        
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
            {
                isBold = !isBold;
                continue;
            }
            
            var run = new Run(part);
            if (isBold)
            {
                run.FontWeight = FontWeights.Bold;
            }
            
            paragraph.Inlines.Add(run);
            isBold = !isBold;
        }
        
        // Если нет жирного текста, добавляем как есть
        if (paragraph.Inlines.Count == 0)
        {
            paragraph.Inlines.Add(new Run(text));
        }
    }
    
    private void ParseInlineFormattingWithBr(string text, Paragraph paragraph)
    {
        // Сначала обрабатываем <br> теги
        var brParts = text.Split(new string[] { "<br>", "<br/>", "<br />" }, StringSplitOptions.None);
        
        for (int i = 0; i < brParts.Length; i++)
        {
            if (i > 0)
            {
                // Добавляем перенос строки
                paragraph.Inlines.Add(new LineBreak());
            }
            
            var part = brParts[i];
            if (!string.IsNullOrEmpty(part))
            {
                // Обрабатываем жирный текст в каждой части
                var boldParts = part.Split(new string[] { "**" }, StringSplitOptions.None);
                bool isBold = false;
                
                foreach (var boldPart in boldParts)
                {
                    if (string.IsNullOrEmpty(boldPart))
                    {
                        isBold = !isBold;
                        continue;
                    }
                    
                    var run = new Run(boldPart);
                    if (isBold)
                    {
                        run.FontWeight = FontWeights.Bold;
                    }
                    
                    paragraph.Inlines.Add(run);
                    isBold = !isBold;
                }
                
                // Если в части не было жирного текста, добавляем как есть
                if (boldParts.Length == 1)
                {
                    // Уже добавлено выше, ничего не делаем
                }
            }
        }
        
        // Если ничего не добавилось, добавляем оригинальный текст
        if (paragraph.Inlines.Count == 0)
        {
            paragraph.Inlines.Add(new Run(text));
        }
    }
}
