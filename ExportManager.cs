using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;
using StudioLog.Models;

namespace StudioLog.Core
{
    public class ExportManager
    {
        public ExportManager()
        {
            // Configure QuestPDF license
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public async Task ExportToPdf(string filepath, string sessionName, string date, string location, List<TimecodeLogEntry> entries)
        {
            await Task.Run(() =>
            {
                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.Letter);
                        page.Margin(40);
                        
                        page.Content().Column(column =>
                        {
                            // Header section with Artist/Date/Location
                            column.Item().Row(row =>
                            {
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().Text("SESSION NAME").FontSize(10).Bold();
                                    col.Item().Text(sessionName).FontSize(10).Italic();
                                });
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().Text("DATE").FontSize(10).Bold();
                                    col.Item().Text(date).FontSize(10).Italic();
                                });
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().Text("LOCATION").FontSize(10).Bold();
                                    col.Item().Text(location).FontSize(10).Italic();
                                });
                            });

                            column.Item().PaddingVertical(10);

                            // Table with data
                            column.Item().Table(table =>
                            {
                                // Define columns
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(2); // TIMECODE IN
                                    columns.RelativeColumn(2); // TIMECODE OUT
                                    columns.RelativeColumn(2); // DURATION
                                    columns.RelativeColumn(3); // CLIP NAME
                                    columns.RelativeColumn(2); // MARK
                                    columns.RelativeColumn(3); // NOTES
                                });

                                // Header row
                                table.Header(header =>
                                {
                                    header.Cell().Background("#1e1e1e").Padding(5).Text("TIMECODE IN").FontColor("#ffffff").FontSize(9).Bold();
                                    header.Cell().Background("#1e1e1e").Padding(5).Text("TIMECODE OUT").FontColor("#ffffff").FontSize(9).Bold();
                                    header.Cell().Background("#1e1e1e").Padding(5).Text("DURATION").FontColor("#ffffff").FontSize(9).Bold();
                                    header.Cell().Background("#1e1e1e").Padding(5).Text("CLIP NAME").FontColor("#ffffff").FontSize(9).Bold();
                                    header.Cell().Background("#1e1e1e").Padding(5).Text("MARK").FontColor("#ffffff").FontSize(9).Bold();
                                    header.Cell().Background("#1e1e1e").Padding(5).Text("NOTES").FontColor("#ffffff").FontSize(9).Bold();
                                });

                                // Data rows
                                foreach (var entry in entries)
                                {
                                    table.Cell().Border(1).BorderColor("#555555").Padding(5).Text(entry.TimeCodeIn).FontSize(8);
                                    table.Cell().Border(1).BorderColor("#555555").Padding(5).Text(entry.TimeCodeOut).FontSize(8);
                                    table.Cell().Border(1).BorderColor("#555555").Padding(5).Text(entry.Duration).FontSize(8);
                                    table.Cell().Border(1).BorderColor("#555555").Padding(5).Text(entry.ClipName ?? "").FontSize(8);
                                    table.Cell().Border(1).BorderColor("#555555").Padding(5).Text(entry.MarkTimecode ?? "").FontSize(8);
                                    table.Cell().Border(1).BorderColor("#555555").Padding(5).Text(entry.Notes ?? "").FontSize(8);
                                }
                            });
                        });
                    });
                })
                .GeneratePdf(filepath);
            });
        }

        public async Task ExportToCsv(string filepath, string sessionName, string date, string location, List<TimecodeLogEntry> entries)
        {
            await Task.Run(() =>
            {
                var sb = new StringBuilder();
                
                // Header with metadata
                sb.AppendLine($"SESSION NAME,\"{EscapeCsv(sessionName)}\"");
                sb.AppendLine($"DATE,\"{EscapeCsv(date)}\"");
                sb.AppendLine($"LOCATION,\"{EscapeCsv(location)}\"");
                sb.AppendLine();
                
                // Column headers
                sb.AppendLine("TIMECODE IN,TIMECODE OUT,DURATION,CLIP NAME,MARK,NOTES");
                
                // Data rows
                foreach (var entry in entries)
                {
                    sb.AppendLine($"\"{EscapeCsv(entry.TimeCodeIn)}\",\"{EscapeCsv(entry.TimeCodeOut)}\",\"{EscapeCsv(entry.Duration)}\",\"{EscapeCsv(entry.ClipName)}\",\"{EscapeCsv(entry.MarkTimecode)}\",\"{EscapeCsv(entry.Notes)}\"");
                }
                
                File.WriteAllText(filepath, sb.ToString());
            });
        }

        public async Task ExportToPng(string filepath, string sessionName, string date, string location, List<TimecodeLogEntry> entries)
        {
            await Task.Run(() =>
            {
                int width = 1200;
                int rowHeight = 40;
                int headerHeight = 80;
                int height = headerHeight + (entries.Count + 1) * rowHeight + 40; // +1 for column headers, +40 for padding

                using var surface = SKSurface.Create(new SKImageInfo(width, height));
                var canvas = surface.Canvas;
                
                // Background
                canvas.Clear(SKColors.White);
                
                // Paint for text
                using var textPaint = new SKPaint
                {
                    Color = SKColors.Black,
                    TextSize = 12,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName("Arial")
                };
                
                using var boldPaint = new SKPaint
                {
                    Color = SKColors.Black,
                    TextSize = 12,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
                };
                
                using var headerPaint = new SKPaint
                {
                    Color = SKColors.White,
                    TextSize = 11,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
                };
                
                // Draw metadata header
                int y = 20;
                canvas.DrawText("SESSION NAME", 20, y, boldPaint);
                canvas.DrawText(sessionName, 150, y, textPaint);
                
                canvas.DrawText("DATE", 420, y, boldPaint);
                canvas.DrawText(date, 520, y, textPaint);
                
                canvas.DrawText("LOCATION", 720, y, boldPaint);
                canvas.DrawText(location, 850, y, textPaint);
                
                y += 40;
                
                // Draw table
                using var borderPaint = new SKPaint
                {
                    Color = SKColor.Parse("#555555"),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1,
                    IsAntialias = true
                };
                
                using var headerBgPaint = new SKPaint
                {
                    Color = SKColor.Parse("#1e1e1e"),
                    Style = SKPaintStyle.Fill
                };
                
                // Column widths
                int[] colWidths = { 150, 150, 120, 250, 120, 390 };
                int[] colX = new int[6];
                colX[0] = 20;
                for (int i = 1; i < 6; i++)
                    colX[i] = colX[i - 1] + colWidths[i - 1];
                
                // Header row
                var headerRect = new SKRect(20, y, width - 20, y + rowHeight);
                canvas.DrawRect(headerRect, headerBgPaint);
                canvas.DrawRect(headerRect, borderPaint);
                
                string[] headers = { "TIMECODE IN", "TIMECODE OUT", "DURATION", "CLIP NAME", "MARK", "NOTES" };
                for (int i = 0; i < headers.Length; i++)
                {
                    canvas.DrawText(headers[i], colX[i] + 5, y + 25, headerPaint);
                    if (i < headers.Length - 1)
                        canvas.DrawLine(colX[i + 1], y, colX[i + 1], y + rowHeight, borderPaint);
                }
                
                y += rowHeight;
                
                // Data rows
                foreach (var entry in entries)
                {
                    var rowRect = new SKRect(20, y, width - 20, y + rowHeight);
                    canvas.DrawRect(rowRect, borderPaint);
                    
                    canvas.DrawText(entry.TimeCodeIn ?? "", colX[0] + 5, y + 25, textPaint);
                    canvas.DrawText(entry.TimeCodeOut ?? "", colX[1] + 5, y + 25, textPaint);
                    canvas.DrawText(entry.Duration ?? "", colX[2] + 5, y + 25, textPaint);
                    canvas.DrawText(entry.ClipName ?? "", colX[3] + 5, y + 25, textPaint);
                    canvas.DrawText(entry.MarkTimecode ?? "", colX[4] + 5, y + 25, textPaint);
                    canvas.DrawText(entry.Notes ?? "", colX[5] + 5, y + 25, textPaint);
                    
                    // Draw column separators
                    for (int i = 1; i < 6; i++)
                        canvas.DrawLine(colX[i], y, colX[i], y + rowHeight, borderPaint);
                    
                    y += rowHeight;
                }
                
                // Save to PNG
                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = File.OpenWrite(filepath);
                data.SaveTo(stream);
            });
        }

        private string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            
            // Replace newlines with spaces to prevent breaking CSV row structure
            // (Notes TextBox has AcceptsReturn=True, so multiline content is possible)
            value = value.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
            
            // Escape quotes by doubling them
            return value.Replace("\"", "\"\"");
        }
    }
}
