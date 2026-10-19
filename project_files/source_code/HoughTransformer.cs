using Avalonia.Controls;

namespace ImageApp
{
    public static class HoughTransformer
    {
        public static int[,] HoughTransform(byte[,] inputImage, bool binary, int kNumber, int dStep)
        {
            int w = inputImage.GetLength(0);
            int h = inputImage.GetLength(1);
            int[,] output = new int[kNumber,dStep];
            return output;
        }
    } 
}
