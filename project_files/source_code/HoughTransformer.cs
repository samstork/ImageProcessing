using System.Diagnostics;

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
        /// <returns>A 2D array of grayscale integer intensities with values.</returns>
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

		public static int[][,] HoughCircleTransform(byte[,] inputImage, bool binary, int kNumber, int dNumber, int minRadius, int maxRadius) {
			int w = inputImage.GetLength(0);
			int h = inputImage.GetLength(1);
			int radiusInterval = maxRadius - minRadius;

			int[][,] accumulator = new int[radiusInterval + 1][,];

			// Compute maximum r (diagonal length of img) and divide by the number of d bins.
			float kAngleRad = (float)Math.PI/kNumber;

			// Since we're using the same theta values each loop, we can precompute the sines and cosines and just look them up.
			float[] sines = new float[kNumber];
			float[] cosines = new float[kNumber];
			for (int i = 0; i < kNumber; i++)
			{
				sines[i] = (float)Math.Sin(kAngleRad * i);
				cosines[i] = (float)Math.Cos(kAngleRad * i);
			}

			int a, b;
			byte accumValue = 1;
			for (int radius = 0; radius < radiusInterval; radius++)
			{
				accumulator[radius] = new int[w,h];
				for (int x = 0; x<w; x++)
				for (int y = 0; y<h; y++)
				{
					if (inputImage[x, y] == 0) continue;
					if(!binary) accumValue = inputImage[x, y];
					for (int theta = 0; theta < kNumber; theta++)
					{
						b = y - (int)Math.Round(sines[theta] * (radius + minRadius));
						a = x - (int)Math.Round(cosines[theta] * (radius + minRadius));
						if (0 <= b && b < h && 0 <= a && a < w)
						{
							accumulator[radius][a,b] += accumValue;
						}

					}

				}
			}
			return accumulator;
		}
		// 	Pseudocode from wikipedia (Used as basis)
		// For each A[a,b,r] = 0; // fill with zeroes initially, instantiate 3D matrix
		//   For each cell(x,y)
		//       For each theta t = 0 to 360  // the possible  theta 0 to 360
		//          b = y – r * sin(t * PI / 180);  //polar coordinate for center (convert to radians)
		//          a = x – r * cos(t * PI / 180); //polar coordinate for center (convert to radians)
		//          A[a,b,r] +=1; //voting
		//       end
		//   end


		public static int[,] NormaliseAccumulator(byte[,] inputImage, int[,] accumulator, bool binary)
		{
			int kNumber = accumulator.GetLength(0);
            int dNumber = accumulator.GetLength(1);	
			int w = inputImage.GetLength(0);
            int h = inputImage.GetLength(1);
			int[,] output = new int[kNumber, dNumber];
			byte[,] white = new byte[w,h];
			for(int x=0; x<w; x++)
			for(int y=0; y<h; y++)
			{
				white[x,y] = byte.MaxValue;
			}
			
			int[,] maxValues = HoughTransform(white, binary, kNumber, dNumber);
			// Ignore 5% smallest possible lines
			int globalMaxim = 0;
			for(int x=0; x<kNumber; x++)
			for(int y=0; y<dNumber; y++)
			{
				if (globalMaxim < maxValues[x, y]) globalMaxim = maxValues[x,y];
			}
			int minimumIntensity = Math.Max((int)globalMaxim/20, 1);

			for(int x=0; x<kNumber; x++)
			for(int y=0; y<dNumber; y++)
			{
				if(maxValues[x,y] > minimumIntensity)
				{
					output[x,y] = byte.MaxValue * accumulator[x,y] / maxValues[x,y];
				}
			}
			return output;

		}
		public static byte[,] HoughPeaks(byte[,] accumulator, int peak_threshold)
		{
			int w = accumulator.GetLength(0);
            int h = accumulator.GetLength(1);
			byte[,] output = NonMaxSuppression(accumulator);

			for (int i = 0; i<w; i++)
            for (int j = 0; j<h; j++)
			{
				if(output[i,j] < peak_threshold)
				{
					output[i,j] = 0;
				} else
				{
					output[i,j] = byte.MaxValue;
				}
			}
			return output;
		}

		private static byte[,] NonMaxSuppression(byte[,] inputImage)
        {
            int w = inputImage.GetLength(0);
            int h = inputImage.GetLength(1);

            byte[,] output = new byte[w,h];

			bool[,] plusStruct =
			{
				{ false, true,  false },
				{ true,  false,  true  },
				{ false, true,  false }  
			};
			
			bool localMax;
            // For all pixels in the input image.
            for (int i = 0; i<w; i++)
            for (int j = 0; j<h; j++)
            {
                if (inputImage[i,j] == 0) continue;
				localMax = true;
                // For all pixels in the structuring element.
                for (int u = 0; u < 3; u++)
                for (int v = 0; v < 3; v++)
                {
					if (!plusStruct[u,v]) continue;
                    // Calculate the offset
                    int x_offset = i+u-1;
                    int y_offset = j+v-1;
					
					// Wrap x around like pacman
					if (x_offset < 0)
					{
						x_offset += w;
						y_offset = h - y_offset;
					} else if (x_offset >= w)
					{
						x_offset -= w;
						y_offset = h - y_offset;
					} 

                    // Check if the offset element is within the bounds of the image.
                    if ((x_offset>=0) && (x_offset<w) && (y_offset>=0) && (y_offset<h))
            		{
                        if(inputImage[i, j] < inputImage[x_offset,y_offset])
                        {
                            localMax = false;
							break; 
                        }
                    }
                }
				if (localMax) output[i, j] = inputImage[i,j];
            }

            return output;
        }

		public static List<List<(int x, int y)>> HoughLineSegments(byte[,] edgeImage, byte[,] peaks, 
																	byte threshold = 0, 
																	int minSegmentLength = 50, 
																	int maxGapLength = 25)
		{
			int w = edgeImage.GetLength(0);
			int h = edgeImage.GetLength(1);
			int xCentre = w/2;
			int yCentre = h/2;

			int kNumber = peaks.GetLength(0);
			int dNumber = peaks.GetLength(1);
			int dCentre = dNumber/2;

			float dDistancePix = (float)Math.Sqrt(w*w + h*h) / dNumber;
			float kAngleRad = (float)Math.PI/kNumber;

			double cosine, sine;
			double sqrt2 = Math.Sqrt(2)/2;

			List<(int x, int y)> segment = new List<(int x, int y)>();
			List<List<(int x, int y)>> segmentList = new List<List<(int x, int y)>>();

			bool debug = false;
			int x, y, gapCounter;
			float r;
			for (int theta_raw = 0; theta_raw<kNumber; theta_raw++)
            for (int r_raw = 0; r_raw<dNumber; r_raw++)
			{
				if(peaks[theta_raw, r_raw] == 0) continue;
				segment.Clear();
				sine = Math.Sin(theta_raw * kAngleRad);
				cosine = Math.Cos(theta_raw * kAngleRad);
				r = (r_raw - dCentre) * dDistancePix;
				gapCounter = -1;
				if (Math.Abs(sine) >= sqrt2)
				{
					for (x = 0; x<w; x++)
					{
						y = (int)Math.Round((r - (x-xCentre) * cosine)/sine) + yCentre;
						if(!(0 <= y && y < h)) continue;
						if(edgeImage[x, y] > threshold || debug)
						{
							gapCounter = 0;
							segment.Add((x, y));
						} else if (gapCounter >= 0 && gapCounter < maxGapLength)
						{
							segment.Add((x, y));
							gapCounter ++;
						} else if (gapCounter >= maxGapLength)
						{
							segment.RemoveRange(segment.Count-gapCounter, gapCounter);
							if (segment.Count >= minSegmentLength) segmentList.Add([.. segment]);
							gapCounter = -1;
							segment.Clear();
						}

					}
				} else
				{
					for (y = 0; y<h; y++)
					{
						x = (int)Math.Round((r - (y-yCentre) * sine)/cosine) + xCentre;
						if(!(0 <= x && x < w)) continue;
						if(edgeImage[x, y] > threshold || debug)
						{
							gapCounter = 0;
							segment.Add((x, y));
						} else if (gapCounter >= 0 && gapCounter < maxGapLength)
						{
							segment.Add((x, y));
							gapCounter ++;
						} else if (gapCounter >= maxGapLength)
						{
							segment.RemoveRange(segment.Count-gapCounter, gapCounter);
							if (segment.Count >= minSegmentLength) segmentList.Add([.. segment]);
							gapCounter = -1;
							segment.Clear();
						}
					}
				}
				if(gapCounter > 0) segment.RemoveRange(segment.Count-gapCounter, gapCounter);
				if (segment.Count >= minSegmentLength) segmentList.Add([.. segment]);
			}
			return segmentList;
		}
		public static byte[,,] VisualiseSegments(byte[,] edgeImage, List<List<(int x, int y)>> segmentList)
		{
			int w = edgeImage.GetLength(0);
			int h = edgeImage.GetLength(1);
			byte[,,] output = new byte[w,h,3];
			for(int x = 0; x<w; x++)
			for(int y = 0; y<h; y++)
			{
				output[x,y,0] = edgeImage[x,y];
				output[x,y,1] = edgeImage[x,y];
				output[x,y,2] = edgeImage[x,y];
			}

			foreach(List<(int x, int y)> segment in segmentList)
			{
				foreach((int x, int y) coords in segment)
				{
					output[coords.x, coords.y, 0] = byte.MaxValue;
					output[coords.x, coords.y, 1] = 0;
					output[coords.x, coords.y, 2] = 0;
				}
			}

			return output;
		}
	} 
}
