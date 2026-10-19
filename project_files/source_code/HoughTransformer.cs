namespace ImageApp
{
	public static class HoughTransformer
	{
		/// <summary>
        /// Computes hough transform of binary or grayscale image. 
        /// </summary>
        /// <param name="inputImage">The input image.</param>
		/// <param name="binary">Whether the image is binary.</param>
		/// <param name="kNumber">The number of theta to check. Theta step = 2pi/kNumber. Width of output.</param>
		/// <param name="dNumber">The number of possible r. Step size = diagonal
        /// <returns>A 2D array of grayscale integer intensities with values in [0, 255].</returns>
		public static int[,] HoughTransform(byte[,] inputImage, bool binary, int kNumber, int dNumber) {
			int w = inputImage.GetLength(0);
			int h = inputImage.GetLength(1);
			int[,] accumulator = new int[kNumber, dNumber];
			int xCentre = w/2;
			int yCentre = h/2;
			// Make it even if it's not to find an integer center adjustment
			dNumber += dNumber%2;
			int dCentre = dNumber/2;

			// Compute maximum r (diagonal length of img) and divide by the number of d bins. 
			float dDistancePix = (float)Math.Sqrt(w*w + h*h) / dNumber;
			float kAngleRad = (float)Math.PI/kNumber;
			
			// Since we're using the same theta values each loop, we can precompute the sines and cosines and just look them up. 
			float[] sines = new float[kNumber];
			float[] cosines = new float[kNumber];
			for (int i = 0; i < kNumber; i++)
			{
				sines[i] = (float)Math.Sin(kAngleRad * i);
				cosines[i] = (float)Math.Cos(kAngleRad * i);
			}

			int oX, oY, r;
			float rf;
			byte accumValue = 1;
			for (int i = 0; i<w; i++)
            for (int j = 0; j<h; j++)
			{
				// Check for background pixels
				if(inputImage[i, j] == 0) continue;
				if(!binary) accumValue = inputImage[i, j];
				// Adjust origin for r. Not sure why this is helpful, I think top left origin would work just as well.
				oX = i - xCentre;
				oY = j - yCentre;

				// Check each possible theta (as defined by our limited k)
				for (int theta = 0; theta<kNumber; theta++)
				{
					// Compute r for the current theta
					rf = oX * cosines[theta] + oY * sines[theta];
					// Quantize r to nearest bin, add adjustment to handle negative r. Clamp just in case. 
					r = (int)Math.Clamp(Math.Round(rf/dDistancePix) + dCentre, 0, dNumber - 1);

					// Increment accumulator. Check if it's not going to overflow first.
					if(accumulator[theta, r] < int.MaxValue - byte.MaxValue) accumulator[theta, r] += accumValue;

				}
			}
			return accumulator;
		}

	} 
}
