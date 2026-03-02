using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace MinimalApiCall;

/// <summary>
/// PPT生成服务 - 使用OpenXML创建PowerPoint文件
/// </summary>
public class PptGeneratorService
{
    private readonly string _outputPath;

    public PptGeneratorService(string outputPath)
    {
        _outputPath = outputPath;
        if (!Directory.Exists(_outputPath))
        {
            Directory.CreateDirectory(_outputPath);
        }
    }

    /// <summary>
    /// 从Markdown内容生成PPT
    /// </summary>
    public string GenerateFromMarkdown(string title, string content)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var safeTitle = SanitizeFileName(title);
        var fileName = $"{safeTitle}_{timestamp}.pptx";
        var filePath = Path.Combine(_outputPath, fileName);

        // 解析Markdown为幻灯片数据
        var slides = ParseMarkdownToSlides(content);

        // 创建PPT
        CreatePowerPoint(filePath, title, slides);

        return filePath;
    }

    /// <summary>
    /// 从结构化数据生成PPT
    /// </summary>
    public string GenerateFromData(string title, List<SlideData> slides)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var safeTitle = SanitizeFileName(title);
        var fileName = $"{safeTitle}_{timestamp}.pptx";
        var filePath = Path.Combine(_outputPath, fileName);

        CreatePowerPoint(filePath, title, slides);

        return filePath;
    }

    /// <summary>
    /// 解析Markdown为幻灯片数据
    /// </summary>
    private List<SlideData> ParseMarkdownToSlides(string markdown)
    {
        var slides = new List<SlideData>();
        var lines = markdown.Split('\n');
        
        SlideData? currentSlide = null;
        var contentBuilder = new StringBuilder();
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // 检测标题（##）作为新幻灯片
            if (trimmedLine.StartsWith("## "))
            {
                // 保存前一个幻灯片
                if (currentSlide != null && contentBuilder.Length > 0)
                {
                    currentSlide.Content = contentBuilder.ToString().Trim();
                    slides.Add(currentSlide);
                }
                
                // 创建新幻灯片
                currentSlide = new SlideData
                {
                    Title = trimmedLine.Substring(3).Trim()
                };
                contentBuilder.Clear();
            }
            // 检测一级标题（#）作为封面
            else if (trimmedLine.StartsWith("# "))
            {
                if (currentSlide != null && contentBuilder.Length > 0)
                {
                    currentSlide.Content = contentBuilder.ToString().Trim();
                    slides.Add(currentSlide);
                }
                
                currentSlide = new SlideData
                {
                    Title = trimmedLine.Substring(2).Trim(),
                    IsTitle = true
                };
                contentBuilder.Clear();
            }
            else if (!string.IsNullOrWhiteSpace(trimmedLine))
            {
                // 清理Markdown格式
                var cleanLine = CleanMarkdown(trimmedLine);
                contentBuilder.AppendLine(cleanLine);
            }
        }
        
        // 添加最后一个幻灯片
        if (currentSlide != null)
        {
            currentSlide.Content = contentBuilder.ToString().Trim();
            slides.Add(currentSlide);
        }

        return slides;
    }

    /// <summary>
    /// 清理Markdown格式
    /// </summary>
    private string CleanMarkdown(string text)
    {
        // 移除粗体标记
        text = text.Replace("**", "");
        text = text.Replace("__", "");
        
        // 移除斜体标记
        text = text.Replace("*", "");
        text = text.Replace("_", "");
        
        // 移除代码标记
        text = text.Replace("`", "");
        
        // 移除链接格式 [text](url)
        while (text.Contains("[") && text.Contains("]") && text.Contains("(") && text.Contains(")"))
        {
            var start = text.IndexOf('[');
            var middle = text.IndexOf(']', start);
            var urlStart = text.IndexOf('(', middle);
            var end = text.IndexOf(')', urlStart);
            
            if (start < middle && middle < urlStart && urlStart < end)
            {
                var linkText = text.Substring(start + 1, middle - start - 1);
                text = text.Substring(0, start) + linkText + text.Substring(end + 1);
            }
            else
            {
                break;
            }
        }
        
        return text;
    }

    /// <summary>
    /// 创建PowerPoint文件
    /// </summary>
    private void CreatePowerPoint(string filePath, string documentTitle, List<SlideData> slides)
    {
        using (var presentationDocument = PresentationDocument.Create(filePath, PresentationDocumentType.Presentation))
        {
            // 创建演示文稿部分
            var presentationPart = presentationDocument.AddPresentationPart();
            presentationPart.Presentation = new Presentation();

            // 创建幻灯片ID列表
            var slideIdList = new SlideIdList();
            presentationPart.Presentation.SlideIdList = slideIdList;

            uint slideId = 256;

            // 添加每个幻灯片
            foreach (var slideData in slides)
            {
                var slidePart = CreateSlidePart(presentationPart);
                
                if (slideData.IsTitle)
                {
                    CreateTitleSlide(slidePart, slideData.Title, documentTitle);
                }
                else
                {
                    CreateContentSlide(slidePart, slideData.Title, slideData.Content);
                }

                var slideId1 = new SlideId { Id = slideId++, RelationshipId = presentationPart.GetIdOfPart(slidePart) };
                slideIdList.Append(slideId1);
            }

            presentationPart.Presentation.Save();
        }
    }

    /// <summary>
    /// 创建幻灯片部分
    /// </summary>
    private SlidePart CreateSlidePart(PresentationPart presentationPart)
    {
        var slidePart = presentationPart.AddNewPart<SlidePart>();
        slidePart.Slide = new Slide(new CommonSlideData(new ShapeTree()));
        return slidePart;
    }

    /// <summary>
    /// 创建封面幻灯片
    /// </summary>
    private void CreateTitleSlide(SlidePart slidePart, string title, string subtitle)
    {
        var slide = slidePart.Slide;
        var shapeTree = slide.CommonSlideData!.ShapeTree!;

        // 添加渐变背景
        AddGradientBackground(slidePart);

        // 添加装饰性矩形条
        var decorRect = CreateRectangle(0, 2743200, 9144000, 800000, "1F4E78"); // 深蓝色条
        shapeTree.Append(decorRect);

        // 添加主标题（白色大字）
        var titleShape = CreateStyledTextBox(914400, 1828800, 7315200, 1400000, title, 5400, true, "FFFFFF");
        shapeTree.Append(titleShape);

        // 添加副标题
        if (!string.IsNullOrEmpty(subtitle))
        {
            var subtitleShape = CreateStyledTextBox(914400, 3500000, 7315200, 800000, subtitle, 2800, false, "E7E6E6");
            shapeTree.Append(subtitleShape);
        }

        // 添加底部装饰线
        var bottomLine = CreateLine(914400, 5400000, 7315200, "4472C4", 38100);
        shapeTree.Append(bottomLine);

        slide.Save();
    }

    /// <summary>
    /// 创建内容幻灯片
    /// </summary>
    private void CreateContentSlide(SlidePart slidePart, string title, string content)
    {
        var slide = slidePart.Slide;
        var shapeTree = slide.CommonSlideData!.ShapeTree!;

        // 添加渐变背景
        AddGradientBackground(slidePart);

        // 添加顶部装饰条
        var topBar = CreateRectangle(0, 0, 9144000, 120000, "4472C4"); // 蓝色顶部条
        shapeTree.Append(topBar);

        // 添加标题背景矩形
        var titleBg = CreateRectangle(457200, 400000, 8229600, 900000, "2E5090", 90); // 半透明深蓝背景
        shapeTree.Append(titleBg);

        // 添加标题（白色）
        var titleShape = CreateStyledTextBox(600000, 450000, 7900000, 800000, title, 3600, true, "FFFFFF");
        shapeTree.Append(titleShape);

        // 添加内容区域背景（白色半透明）
        var contentBg = CreateRectangle(457200, 1500000, 8229600, 4200000, "FFFFFF", 95);
        shapeTree.Append(contentBg);

        // 添加内容文本
        var contentLines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var contentText = ProcessContentLines(contentLines);
        var contentShape = CreateBulletTextBox(700000, 1700000, 7700000, 3800000, contentText, 2000, "1F4E78");
        shapeTree.Append(contentShape);

        // 添加底部装饰元素
        var bottomCircle = CreateCircle(8000000, 5800000, 300000, "5B9BD5");
        shapeTree.Append(bottomCircle);

        slide.Save();
    }

    /// <summary>
    /// 添加渐变背景
    /// </summary>
    private void AddGradientBackground(SlidePart slidePart)
    {
        var slide = slidePart.Slide;
        var commonSlideData = slide.CommonSlideData ?? new CommonSlideData();
        var background = new P.Background();
        
        var backgroundProperties = new P.BackgroundProperties();
        var gradientFill = new A.GradientFill { RotateWithShape = true };
        
        var gradientStopList = new A.GradientStopList();
        
        // 第一个渐变点 - 深蓝色
        var gradientStop1 = new A.GradientStop { Position = 0 };
        var schemeColor1 = new A.SchemeColor { Val = A.SchemeColorValues.Accent1 };
        var lumMod1 = new A.LuminanceModulation { Val = 75000 };
        schemeColor1.Append(lumMod1);
        gradientStop1.Append(schemeColor1);
        
        // 第二个渐变点 - 浅蓝色
        var gradientStop2 = new A.GradientStop { Position = 100000 };
        var schemeColor2 = new A.SchemeColor { Val = A.SchemeColorValues.Accent1 };
        var lumMod2 = new A.LuminanceModulation { Val = 115000 };
        var satMod2 = new A.SaturationModulation { Val = 150000 };
        schemeColor2.Append(lumMod2);
        schemeColor2.Append(satMod2);
        gradientStop2.Append(schemeColor2);
        
        gradientStopList.Append(gradientStop1);
        gradientStopList.Append(gradientStop2);
        
        var linearGradientFill = new A.LinearGradientFill { Angle = 5400000, Scaled = false };
        
        gradientFill.Append(gradientStopList);
        gradientFill.Append(linearGradientFill);
        
        backgroundProperties.Append(gradientFill);
        background.Append(backgroundProperties);
        
        commonSlideData.Background = background;
        slide.CommonSlideData = commonSlideData;
    }

    /// <summary>
    /// 创建矩形形状
    /// </summary>
    private P.Shape CreateRectangle(long x, long y, long width, long height, string hexColor, int alpha = 100)
    {
        var shape = new P.Shape();

        var nonVisualShapeProperties = new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = (UInt32Value)GenerateUniqueId(), Name = "Rectangle" },
            new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
            new ApplicationNonVisualDrawingProperties()
        );

        var shapeProperties = new P.ShapeProperties();
        var transform2D = new A.Transform2D();
        transform2D.Append(new A.Offset { X = x, Y = y });
        transform2D.Append(new A.Extents { Cx = width, Cy = height });
        
        var presetGeometry = new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle };
        presetGeometry.Append(new A.AdjustValueList());
        
        // 设置填充颜色
        var solidFill = new A.SolidFill();
        var rgbColor = new A.RgbColorModelHex { Val = hexColor };
        if (alpha < 100)
        {
            rgbColor.Append(new A.Alpha { Val = alpha * 1000 });
        }
        solidFill.Append(rgbColor);
        
        // 无边框
        var outline = new A.Outline { Width = 0 };
        outline.Append(new A.NoFill());
        
        shapeProperties.Append(transform2D);
        shapeProperties.Append(presetGeometry);
        shapeProperties.Append(solidFill);
        shapeProperties.Append(outline);

        var textBody = new P.TextBody();
        textBody.Append(new A.BodyProperties());
        textBody.Append(new A.ListStyle());
        textBody.Append(new A.Paragraph(new A.EndParagraphRunProperties()));

        shape.Append(nonVisualShapeProperties);
        shape.Append(shapeProperties);
        shape.Append(textBody);

        return shape;
    }

    /// <summary>
    /// 创建圆形形状
    /// </summary>
    private P.Shape CreateCircle(long x, long y, long diameter, string hexColor)
    {
        var shape = new P.Shape();

        var nonVisualShapeProperties = new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = (UInt32Value)GenerateUniqueId(), Name = "Circle" },
            new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
            new ApplicationNonVisualDrawingProperties()
        );

        var shapeProperties = new P.ShapeProperties();
        var transform2D = new A.Transform2D();
        transform2D.Append(new A.Offset { X = x, Y = y });
        transform2D.Append(new A.Extents { Cx = diameter, Cy = diameter });
        
        var presetGeometry = new A.PresetGeometry { Preset = A.ShapeTypeValues.Ellipse };
        presetGeometry.Append(new A.AdjustValueList());
        
        var solidFill = new A.SolidFill();
        solidFill.Append(new A.RgbColorModelHex { Val = hexColor });
        
        var outline = new A.Outline { Width = 0 };
        outline.Append(new A.NoFill());
        
        shapeProperties.Append(transform2D);
        shapeProperties.Append(presetGeometry);
        shapeProperties.Append(solidFill);
        shapeProperties.Append(outline);

        var textBody = new P.TextBody();
        textBody.Append(new A.BodyProperties());
        textBody.Append(new A.ListStyle());
        textBody.Append(new A.Paragraph(new A.EndParagraphRunProperties()));

        shape.Append(nonVisualShapeProperties);
        shape.Append(shapeProperties);
        shape.Append(textBody);

        return shape;
    }

    /// <summary>
    /// 创建线条
    /// </summary>
    private P.ConnectionShape CreateLine(long x, long y, long width, string hexColor, int lineWidth)
    {
        var connShape = new P.ConnectionShape();

        var nonVisualConnectionShapeProperties = new P.NonVisualConnectionShapeProperties(
            new P.NonVisualDrawingProperties { Id = (UInt32Value)GenerateUniqueId(), Name = "Line" },
            new P.NonVisualConnectorShapeDrawingProperties(),
            new ApplicationNonVisualDrawingProperties()
        );

        var shapeProperties = new P.ShapeProperties();
        var transform2D = new A.Transform2D();
        transform2D.Append(new A.Offset { X = x, Y = y });
        transform2D.Append(new A.Extents { Cx = width, Cy = 0 });
        
        var presetGeometry = new A.PresetGeometry { Preset = A.ShapeTypeValues.Line };
        presetGeometry.Append(new A.AdjustValueList());
        
        var outline = new A.Outline { Width = lineWidth, CapType = A.LineCapValues.Flat };
        var solidFill = new A.SolidFill();
        solidFill.Append(new A.RgbColorModelHex { Val = hexColor });
        outline.Append(solidFill);
        
        shapeProperties.Append(transform2D);
        shapeProperties.Append(presetGeometry);
        shapeProperties.Append(outline);

        connShape.Append(nonVisualConnectionShapeProperties);
        connShape.Append(shapeProperties);

        return connShape;
    }

    private uint _uniqueIdCounter = 10;
    private uint GenerateUniqueId() => _uniqueIdCounter++;

    /// <summary>
    /// 处理内容行（添加项目符号）
    /// </summary>
    private string ProcessContentLines(string[] lines)
    {
        var result = new StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // 如果行已经以项目符号开始，保留；否则添加
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("• ") || 
                trimmed.StartsWith("* ") || char.IsDigit(trimmed[0]))
            {
                result.AppendLine(trimmed);
            }
            else
            {
                result.AppendLine($"• {trimmed}");
            }
        }
        return result.ToString().Trim();
    }

    /// <summary>
    /// 创建带样式的文本框
    /// </summary>
    private P.Shape CreateStyledTextBox(long x, long y, long width, long height, string text, int fontSize, bool isBold, string textColor)
    {
        var shape = new P.Shape();

        var nonVisualShapeProperties = new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = (UInt32Value)GenerateUniqueId(), Name = "TextBox" },
            new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
            new ApplicationNonVisualDrawingProperties()
        );

        var shapeProperties = new P.ShapeProperties();
        var transform2D = new A.Transform2D();
        transform2D.Append(new A.Offset { X = x, Y = y });
        transform2D.Append(new A.Extents { Cx = width, Cy = height });
        
        // 无填充和边框
        shapeProperties.Append(transform2D);
        shapeProperties.Append(new A.NoFill());
        var outline = new A.Outline { Width = 0 };
        outline.Append(new A.NoFill());
        shapeProperties.Append(outline);

        var textBody = new P.TextBody();
        textBody.Append(new A.BodyProperties 
        { 
            Wrap = A.TextWrappingValues.Square,
            Anchor = A.TextAnchoringTypeValues.Center
        });
        textBody.Append(new A.ListStyle());

        var paragraph = new A.Paragraph();
        var paragraphProperties = new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Left };
        paragraph.Append(paragraphProperties);
        
        var run = new A.Run();
        var runProperties = new A.RunProperties 
        { 
            Language = "zh-CN", 
            FontSize = fontSize,
            Bold = isBold
        };
        
        var solidFill = new A.SolidFill();
        solidFill.Append(new A.RgbColorModelHex { Val = textColor });
        runProperties.Append(solidFill);
        
        // 使用更好的字体
        runProperties.Append(new A.LatinFont { Typeface = "微软雅黑" });
        runProperties.Append(new A.EastAsianFont { Typeface = "微软雅黑" });
        
        run.Append(runProperties);
        run.Append(new A.Text(text));
        
        paragraph.Append(run);
        textBody.Append(paragraph);

        shape.Append(nonVisualShapeProperties);
        shape.Append(shapeProperties);
        shape.Append(textBody);

        return shape;
    }

    /// <summary>
    /// 创建带项目符号的文本框
    /// </summary>
    private P.Shape CreateBulletTextBox(long x, long y, long width, long height, string text, int fontSize, string textColor)
    {
        var shape = new P.Shape();

        var nonVisualShapeProperties = new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = (UInt32Value)GenerateUniqueId(), Name = "BulletTextBox" },
            new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
            new ApplicationNonVisualDrawingProperties()
        );

        var shapeProperties = new P.ShapeProperties();
        var transform2D = new A.Transform2D();
        transform2D.Append(new A.Offset { X = x, Y = y });
        transform2D.Append(new A.Extents { Cx = width, Cy = height });
        shapeProperties.Append(transform2D);
        shapeProperties.Append(new A.NoFill());
        
        var outline = new A.Outline { Width = 0 };
        outline.Append(new A.NoFill());
        shapeProperties.Append(outline);

        var textBody = new P.TextBody();
        textBody.Append(new A.BodyProperties 
        { 
            Wrap = A.TextWrappingValues.Square,
            LeftInset = 91440,
            TopInset = 45720,
            RightInset = 91440,
            BottomInset = 45720
        });
        textBody.Append(new A.ListStyle());

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var paragraph = new A.Paragraph();
            
            // 段落属性 - 项目符号
            var paragraphProperties = new A.ParagraphProperties 
            { 
                LeftMargin = 342900,
                Indent = -342900,
                Alignment = A.TextAlignmentTypeValues.Left
            };
            
            var bulletFont = new A.BulletFont { Typeface = "Arial" };
            var bulletChar = new A.CharacterBullet { Char = "•" };
            var spaceBefore = new A.SpaceBefore();
            spaceBefore.Append(new A.SpacingPoints { Val = 0 });
            var spaceAfter = new A.SpaceAfter();
            spaceAfter.Append(new A.SpacingPoints { Val = 400 });
            
            paragraphProperties.Append(bulletFont);
            paragraphProperties.Append(bulletChar);
            paragraphProperties.Append(spaceBefore);
            paragraphProperties.Append(spaceAfter);
            paragraph.Append(paragraphProperties);
            
            var run = new A.Run();
            var runProperties = new A.RunProperties 
            { 
                Language = "zh-CN", 
                FontSize = fontSize
            };
            
            var solidFill = new A.SolidFill();
            solidFill.Append(new A.RgbColorModelHex { Val = textColor });
            runProperties.Append(solidFill);
            runProperties.Append(new A.LatinFont { Typeface = "微软雅黑" });
            runProperties.Append(new A.EastAsianFont { Typeface = "微软雅黑" });
            
            run.Append(runProperties);
            run.Append(new A.Text(line.TrimStart('•', '-', '*', ' ')));
            
            paragraph.Append(run);
            textBody.Append(paragraph);
        }

        shape.Append(nonVisualShapeProperties);
        shape.Append(shapeProperties);
        shape.Append(textBody);

        return shape;
    }

    /// <summary>
    /// 创建文本框（旧版本兼容）
    /// </summary>
    private P.Shape CreateTextBox(long x, long y, long width, long height, string text, int fontSize, bool isBold)
    {
        var shape = new P.Shape();

        // 非可视形状属性
        var nonVisualShapeProperties = new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = (UInt32Value)2U, Name = "TextBox" },
            new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
            new ApplicationNonVisualDrawingProperties(new PlaceholderShape())
        );

        // 形状属性
        var shapeProperties = new P.ShapeProperties();
        var transform2D = new A.Transform2D();
        transform2D.Append(new A.Offset { X = x, Y = y });
        transform2D.Append(new A.Extents { Cx = width, Cy = height });
        shapeProperties.Append(transform2D);

        // 文本体
        var textBody = new P.TextBody();
        textBody.Append(new A.BodyProperties());
        textBody.Append(new A.ListStyle());

        var paragraph = new A.Paragraph();
        var run = new A.Run();
        
        var runProperties = new A.RunProperties { Language = "zh-CN", FontSize = fontSize };
        if (isBold)
        {
            runProperties.Bold = true;
        }
        
        run.Append(runProperties);
        run.Append(new A.Text(text));
        
        paragraph.Append(run);
        textBody.Append(paragraph);

        shape.Append(nonVisualShapeProperties);
        shape.Append(shapeProperties);
        shape.Append(textBody);

        return shape;
    }

    private string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Replace(" ", "_").Substring(0, Math.Min(sanitized.Length, 50));
    }
}

/// <summary>
/// 幻灯片数据
/// </summary>
public class SlideData
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public bool IsTitle { get; set; } = false;
}
