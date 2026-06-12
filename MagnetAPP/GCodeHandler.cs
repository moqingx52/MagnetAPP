using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Collections.Generic;

public class GCodeHandler
{
    private double x0 = 70; // X轴偏置
    private double y0 = 15; // Y轴偏置
    private const double PIXEL_TO_MM = 2; // 像素到毫米的转换比例-测试版1mm，实际版0.1

    public string ProcessImage(string imagePath)
    {
        try
        {
            // 读取图片
            using (Bitmap bmp = new Bitmap(imagePath))
            {
                //// 检查图片大小
                //if (bmp.Width != 100 || bmp.Height != 100)
                //{
                //    throw new Exception("图片尺寸必须为100x100像素");
                //}

                List<string> gcodeLines = new List<string>();

                // 遍历图片像素
                for (int y = 0; y < bmp.Height; y++)
                {
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        Color pixelColor = bmp.GetPixel(x, y);
                        // 检查是否为黑色像素 (RGB值都接近0)
                        if (pixelColor.R < 30 && pixelColor.G < 30 && pixelColor.B < 30)
                        {
                            // 计算实际坐标（毫米）
                            double xPos = x0 + (x * PIXEL_TO_MM) ;  
                            double yPos = y0 + (y * PIXEL_TO_MM) ;

                            // 生成G代码行
                            gcodeLines.Add($"G0 F600 X{xPos:F1} Y{yPos:F1}");
                        }
                    }
                }

                // 添加G代码结束
                gcodeLines.Add("G0 X0 Y0"); // 返回原点

                // 生成输出文件路径
                string outputPath = Path.ChangeExtension(imagePath, ".gcode");

                // 写入文件
                File.WriteAllLines(outputPath, gcodeLines);

                return outputPath;
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"处理图片时出错: {ex.Message}");
        }
    }
}
