namespace ImageApp
{
	public static class HoughTransformer
	{
		public static int[,] HoughTransform(byte[,] inputImage, bool binary, int kNumber, int dNumber) {
			int w = inputImage.GetLength(0);
			int h = inputImage.GetLength(1);
			int[,] accumulator = new int[kNumber, dNumber];
			int xCentre = w/2;
			int yCentre = h/2;

			float dDistancePix = (float)Math.Sqrt(w*w + h*h) / dNumber;
			float kAngleRad = (float)(2*Math.PI)/kNumber;
			
			float[] sines = new float[kNumber];
			float[] cosines = new float[kNumber];

			for (int i = 0; i < kNumber; i++)
			{
				sines[i] = (float)Math.Sin(kAngleRad * i);
				cosines[i] = (float)Math.Cos(kAngleRad * i);
			}
			int oX, oY, r;
			float rf;
			byte accumValue;
			for (int i = 0; i<w; i++)
            for (int j = 0; j<h; j++)
			{
				if(inputImage[i, j] == 0) continue;

				accumValue = binary ? (byte)1 : inputImage[i, j];
				oX = i - xCentre;
				oY = j - yCentre;

				for (int theta = 0; theta<kNumber; theta++)
				{
					rf = oX * cosines[theta] + oY * sines[theta];
					if(rf == 0) continue;
					r = (int)Math.Clamp(Math.Round(rf/dDistancePix), 0, dNumber - 1);
					if(accumulator[theta, r] < int.MaxValue - byte.MaxValue)
					{
						accumulator[theta, r] += accumValue;
					}
				}
			}
			return accumulator;
		}

	} 
}
