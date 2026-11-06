using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// A candidate bobber found in a frame: the pixels that matched, and the shape of the
    /// group they form.
    /// </summary>
    public class BobberCandidate
    {
        /// <summary>Number of matching pixels in the group.</summary>
        public int Area { get; set; }

        /// <summary>Top left of the bounding box of the group.</summary>
        public Point TopLeft { get; set; }

        /// <summary>Bottom right of the bounding box of the group.</summary>
        public Point BottomRight { get; set; }

        /// <summary>Centre of the group, in bitmap coordinates.</summary>
        public Point Centre { get; set; }

        /// <summary>Width of the bounding box.</summary>
        public int Width => BottomRight.X - TopLeft.X + 1;

        /// <summary>Height of the bounding box.</summary>
        public int Height => BottomRight.Y - TopLeft.Y + 1;

        /// <summary>
        /// How much of the bounding box the group fills. A solid bobber scores high, a
        /// ragged edge of red terrain scores low.
        /// </summary>
        public double FillRatio => (double)Area / (Width * Height);

        /// <summary>
        /// How much of the group survives being eroded by one pixel. The feather is a
        /// filled shape so most of it survives, while the edge of a patch of red terrain
        /// is a thin curve that erodes away almost completely.
        /// </summary>
        public double Solidity { get; set; }
    }

    /// <summary>
    /// The result of looking for the bobber in a frame.
    /// </summary>
    public class BobberDetectionResult
    {
        /// <summary>
        /// The bobber that was found, or null when the frame did not contain one.
        /// </summary>
        public BobberCandidate? Candidate { get; set; }

        /// <summary>
        /// Every pixel that matched, so the user interface can show what the detector
        /// saw.
        /// </summary>
        public List<Point> Matches { get; } = new List<Point>();

        /// <summary>
        /// True when the colour rule matched so much of the frame that it could not be
        /// used to find the bobber and a different test was used instead.
        /// </summary>
        public bool UsedFallback { get; set; }
    }

    /// <summary>
    /// Finds the fishing bobber in a captured frame.
    /// 
    /// The previous implementation matched every pixel with Bitmap.GetPixel and then
    /// scored the matches with an O(n^2) neighbourhood count. Two problems with that:
    /// 
    /// 1. Reading the pixels took about 76ms for a 960x540 frame, so the detector only
    ///    managed about 13 cycles a second.
    /// 
    /// 2. It guarded against a large number of matches by throwing all of them away and
    ///    logging "Too much red in this image". That happens whenever the scene is warm,
    ///    for instance at dusk or near red terrain, and the bobber was then never found
    ///    at all.
    /// 
    /// This implementation reads the pixel buffer directly, which takes about 5ms for the
    /// same frame, and it groups the matches into connected regions instead of scoring
    /// them pairwise. The colour rule the user sets in the settings screen is still the
    /// main test, so nothing changes about how it is tuned. It is only replaced when it
    /// has become useless because it is matching the whole scene, and then the float is
    /// found by looking for a small, solid shape that stands out from the water behind
    /// it.
    /// </summary>
    public class BobberDetector
    {
        /// <summary>
        /// The classifier used to decide whether a pixel is part of the bobber.
        /// </summary>
        public IPixelClassifier Classifier { get; set; } = new PixelClassifier();

        /// <summary>
        /// The bobber is the same size in the game whatever resolution the game is
        /// running at, so it covers a different number of pixels on every screen. The
        /// limits below are therefore given as a fraction of the captured frame rather
        /// than as a number of pixels.
        /// 
        /// The fractions below were measured against a real fishing float, which covers
        /// about 0.9% of the frame and is roughly 9% of the frame width and 5% of its
        /// height. The limits are set generously above that so the float is not
        /// rejected, and the shape tests do the work of telling it apart from the
        /// landscape.
        /// </summary>

        /// <summary>
        /// Largest number of matching pixels that can be a bobber, as a fraction of the
        /// frame.
        /// </summary>
        public double MaxAreaFraction { get; set; } = 0.010;

        /// <summary>Widest a bobber may be, as a fraction of the frame width.</summary>
        public double MaxWidthFraction { get; set; } = 0.110;

        /// <summary>Tallest a bobber may be, as a fraction of the frame height.</summary>
        public double MaxHeightFraction { get; set; } = 0.080;

        /// <summary>
        /// Smallest number of matching pixels that can be a bobber, as a fraction of the
        /// frame.
        /// </summary>
        public double MinAreaFraction { get; set; } = 0.00004;

        /// <summary>
        /// How far from the middle of the frame a candidate may be and still be
        /// considered, as a fraction of the frame width. The float is cast in front of
        /// the character and the instructions ask for it to be in the middle of the
        /// screen, so a candidate well away from the middle is much more likely to be
        /// part of the landscape.
        /// </summary>
        public double MaxCentreDistanceFraction { get; set; } = 0.36;

        /// <summary>
        /// How much of its bounding box a bobber must fill. The feather is a solid blob,
        /// whereas red terrain usually covers its bounding box unevenly.
        /// </summary>
        public double MinFillRatio { get; set; } = 0.35;

        /// <summary>
        /// How solid a bobber has to be. The feather is a filled shape, so eroding it by
        /// one pixel still leaves most of it. The edge of a patch of red terrain is a
        /// thin curve with water in the middle of it, and eroding that leaves almost
        /// nothing.
        /// </summary>
        public double MinSolidity { get; set; } = 0.20;

        /// <summary>
        /// How far from where the bobber was last seen a candidate is still considered
        /// the same bobber, in pixels. The float is the only thing in the frame that
        /// moves, so it does not go far.
        /// </summary>
        public int SearchRadius { get; set; } = 60;

        /// <summary>
        /// The size, in pixels, of the blocks used to estimate the local background of
        /// the scene.
        /// </summary>
        public int BlockSize { get; set; } = 24;

        /// <summary>
        /// Smallest difference from the local background that still counts as a
        /// candidate.
        /// </summary>
        public double MinContrast { get; set; } = 18;

        /// <summary>
        /// Largest difference from the local background that still counts as a candidate.
        /// </summary>
        public double MaxContrast { get; set; } = 90;

        /// <summary>
        /// How much more vivid than its surroundings a pixel has to be to be part of the
        /// float, when the colour rule cannot be used. The feather gains roughly 0.3,
        /// red terrain roughly 0.15.
        /// </summary>
        public double MinSaturationGain { get; set; } = 0.18;

        /// <summary>
        /// Above this proportion of matching pixels the colour rule no longer tells us
        /// anything, because it is matching the whole scene. The rule the user sets in
        /// the settings screen is then set aside and the detector relies on how the bobber
        /// differs from its background.
        /// </summary>
        public double DegenerateMatchRatio { get; set; } = 0.005;

        /// <summary>
        /// How many pixels around a match are treated as part of it. This joins the
        /// pieces of the feather back together, because the fallback test only accepts
        /// the most vivid part of it.
        /// </summary>
        public int DilationRadius { get; set; } = 2;

        /// <summary>
        /// How strongly a candidate is favoured for being near the middle of the frame.
        /// </summary>
        public int CentreBias { get; set; } = 200;

        // Buffers are reused between calls. The bot detects many times a second and
        // allocating these per frame would put a lot of pressure on the garbage
        // collector.
        private int[]? lookupBuffer;
        private bool[]? visitedBuffer;
        private double[]? blockMeanBuffer;
        private readonly Stack<int> stack = new Stack<int>();

        /// <summary>
        /// Looks for the bobber in the given frame.
        /// </summary>
        /// <param name="bitmap">The captured frame.</param>
        /// <param name="previousLocation">
        /// Where the bobber was last seen, or null when it has not been seen yet. When
        /// given, candidates further away than <see cref="SearchRadius"/> are ignored.
        /// </param>
        public BobberDetectionResult Find(Bitmap bitmap, Point? previousLocation)
        {
            var result = new BobberDetectionResult();

            if (bitmap == null || bitmap.Width == 0 || bitmap.Height == 0)
            {
                return result;
            }

            var pixels = TryReadPixels(bitmap);
            if (pixels == null)
            {
                return result;
            }

            FindMatchingPixels(pixels, result);

            if (result.Matches.Count == 0)
            {
                return result;
            }

            var candidates = GroupIntoCandidates(result.Matches, pixels.Width, pixels.Height);
            var frameCentre = new Point(pixels.Width / 2, pixels.Height / 2);
            result.Candidate = SelectBest(candidates, previousLocation, frameCentre, pixels.Width, pixels.Height);

            return result;
        }

        /// <summary>
        /// Marks the pixels that matched on the bitmap, so the user interface can show
        /// what the detector saw. Uses the raw pixel buffer rather than SetPixel, which
        /// would be far too slow.
        /// </summary>
        public void HighlightMatches(Bitmap bitmap, IList<Point> matches, Color colour)
        {
            if (bitmap == null || matches == null || matches.Count == 0)
            {
                return;
            }

            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
            try
            {
                int bytesPerPixel = BytesPerPixel(bitmap.PixelFormat);
                unsafe
                {
                    byte* basePtr = (byte*)data.Scan0;
                    for (int i = 0; i < matches.Count; i++)
                    {
                        Point p = matches[i];
                        if (p.X < 0 || p.Y < 0 || p.X >= bitmap.Width || p.Y >= bitmap.Height)
                        {
                            continue;
                        }

                        byte* pixel = basePtr + p.Y * data.Stride + p.X * bytesPerPixel;
                        pixel[0] = (byte)colour.B;
                        pixel[1] = (byte)colour.G;
                        pixel[2] = (byte)colour.R;
                        if (bytesPerPixel == 4)
                        {
                            pixel[3] = 255;
                        }
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        /// <summary>
        /// Number of bytes each pixel occupies in the given format. Only the formats the
        /// screen capture produces are supported.
        /// </summary>
        private static int BytesPerPixel(PixelFormat format)
        {
            if (format == PixelFormat.Format24bppRgb)
            {
                return 3;
            }

            // 32 bits per pixel covers Format32bppArgb and Format32bppRgb.
            return 4;
        }

        /// <summary>
        /// The captured frame, read into a byte buffer once, so that the pixels can be
        /// examined without the cost of Bitmap.GetPixel.
        /// </summary>
        private sealed class PixelBuffer
        {
            public byte[] Data = Array.Empty<byte>();
            public int Width { get; set; }
            public int Height { get; set; }
            public int Stride { get; set; }
            public int BytesPerPixel { get; set; }
        }

        private PixelBuffer? TryReadPixels(Bitmap bitmap)
        {
            var buffer = new PixelBuffer
            {
                Width = bitmap.Width,
                Height = bitmap.Height
            };

            try
            {
                var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
                try
                {
                    buffer.Stride = data.Stride;
                    buffer.BytesPerPixel = BytesPerPixel(bitmap.PixelFormat);
                    buffer.Data = new byte[data.Stride * data.Height];

                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer.Data, 0, buffer.Data.Length);
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
            catch (Exception)
            {
                // An unsupported pixel format.
                return null;
            }

            return buffer;
        }

        /// <summary>
        /// Works out which pixels are part of the bobber.
        /// 
        /// The colour rule the user sets in the settings screen is the main test, because
        /// it works well in ordinary conditions. It only stops working when the scene is
        /// warm enough that it matches a large part of the screen, at which point it
        /// says nothing about where the float is. The rule is then set aside and the
        /// float is found by looking for a small, solid shape that stands out from the
        /// water behind it.
        /// </summary>
        private void FindMatchingPixels(PixelBuffer pixels, BobberDetectionResult result)
        {
            int width = pixels.Width;
            int height = pixels.Height;
            int blockSize = BlockSize;
            int blocksX = (width + blockSize - 1) / blockSize;
            int blocksY = (height + blockSize - 1) / blockSize;

            // Average colour of each block of screen, used as the local background.
            if (blockMeanBuffer == null || blockMeanBuffer.Length < blocksX * blocksY * 3)
            {
                blockMeanBuffer = new double[blocksX * blocksY * 3];
            }

            double[] blockMean = blockMeanBuffer!;

            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    long sumR = 0, sumG = 0, sumB = 0;
                    int count = 0;

                    int xEnd = Math.Min((bx + 1) * blockSize, width);
                    int yEnd = Math.Min((by + 1) * blockSize, height);

                    for (int y = by * blockSize; y < yEnd; y++)
                    {
                        int rowStart = y * pixels.Stride;
                        for (int x = bx * blockSize; x < xEnd; x++)
                        {
                            int offset = rowStart + x * pixels.BytesPerPixel;
                            sumB += pixels.Data[offset];
                            sumG += pixels.Data[offset + 1];
                            sumR += pixels.Data[offset + 2];
                            count++;
                        }
                    }

                    int index = (by * blocksX + bx) * 3;
                    if (count > 0)
                    {
                        blockMean[index] = sumR / (double)count;
                        blockMean[index + 1] = sumG / (double)count;
                        blockMean[index + 2] = sumB / (double)count;
                    }
                    else
                    {
                        blockMean[index] = 0;
                        blockMean[index + 1] = 0;
                        blockMean[index + 2] = 0;
                    }
                }
            }

            // Count how much of the frame the colour rule matches, and work out how much
            // a pixel has to differ from its background to be interesting. The threshold
            // follows the spread of contrast in this particular frame, because a fixed
            // threshold either misses a low contrast bobber or accepts every edge in the
            // scene.
            var histogram = new int[512];
            long total = 0;
            int absoluteMatches = 0;

            for (int y = 0; y < height; y++)
            {
                int rowStart = y * pixels.Stride;
                for (int x = 0; x < width; x++)
                {
                    int offset = rowStart + x * pixels.BytesPerPixel;
                    double bg = Average(blockMean, blocksX, blockSize, x, y);
                    double contrast = Math.Abs(pixels.Data[offset] + pixels.Data[offset + 1] + pixels.Data[offset + 2] - 3 * bg);
                    int bucket = (int)Math.Min(511, contrast);
                    histogram[bucket]++;
                    total++;

                    if (Classifier.IsMatch(pixels.Data[offset + 2], pixels.Data[offset + 1], pixels.Data[offset]))
                    {
                        absoluteMatches++;
                    }
                }
            }

            long running = 0;
            int percentile = 0;
            for (int i = 0; i < 512; i++)
            {
                running += histogram[i];
                if (running >= total * 995 / 1000)
                {
                    percentile = i;
                    break;
                }
            }

            double threshold = Math.Max(MinContrast, Math.Min(percentile * 0.35, MaxContrast));

            // The colour rule is only useless when it matches a large part of the frame.
            bool useFallback = total > 0 && (double)absoluteMatches / total > DegenerateMatchRatio;
            result.UsedFallback = useFallback;

            var matches = result.Matches;

            for (int y = 0; y < height; y++)
            {
                int rowStart = y * pixels.Stride;
                for (int x = 0; x < width; x++)
                {
                    int offset = rowStart + x * pixels.BytesPerPixel;
                    byte r = pixels.Data[offset + 2];
                    byte g = pixels.Data[offset + 1];
                    byte b = pixels.Data[offset];

                    if (!useFallback)
                    {
                        // The normal case: the colour rule decides.
                        if (!Classifier.IsMatch(r, g, b))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        // The scene is warm and the colour rule matches most of the
                        // screen, so it cannot tell the float apart from the scene. What
                        // still identifies the float is that it is more vivid than the
                        // water behind it.
                        int block = (y / blockSize) * blocksX + (x / blockSize);
                        double bgR = blockMean[block * 3];
                        double bgG = blockMean[block * 3 + 1];
                        double bgB = blockMean[block * 3 + 2];
                        double bg = (bgR + bgG + bgB) / 3.0;

                        double contrast = Math.Abs(r + g + b - 3 * bg);
                        if (contrast < threshold)
                        {
                            continue;
                        }

                        if (!StandsOutFromBackground(r, g, b, bgR, bgG, bgB))
                        {
                            continue;
                        }
                    }

                    matches.Add(new Point(x, y));
                }
            }

            // In the fallback case only the most vivid part of the feather passes the
            // test, which breaks it into pieces. Joining the pieces up again, by
            // treating pixels close to a match as matches too, gives back the whole
            // shape.
            if (useFallback && matches.Count > 0)
            {
                ConnectNearbyMatches(matches, width, height, DilationRadius);
            }
        }

        /// <summary>
        /// True when the pixel stands out from the water behind it.
        /// 
        /// The float is a small, strongly coloured object sitting on the water. What
        /// identifies it is not any particular colour but that it is more vivid than its
        /// surroundings.
        /// 
        /// Comparing the channels one by one does not work. At dusk the whole scene is
        /// reddish, so the green and blue channels of the float differ from the water
        /// behind it by more than the red channel does, and the float stops looking red.
        /// Saturation does not have that problem: the feather is vivid whatever colour
        /// the scene has become. A red feather on deep blue water gains about 0.3 in
        /// saturation, and so does the same feather on a red dusk sky, while red terrain
        /// only gains about 0.12 to 0.19.
        /// </summary>
        private bool StandsOutFromBackground(byte r, byte g, byte b, double bgR, double bgG, double bgB)
        {
            double saturation = Saturation(r, g, b);
            double backgroundSaturation = Saturation(bgR, bgG, bgB);

            if (saturation - backgroundSaturation < MinSaturationGain)
            {
                return false;
            }

            // The float has to be red or blue to be a float. The lava float uses the
            // blue feather, so both are allowed.
            bool redDominant = r > g && r > b;
            bool blueDominant = b > g && b > r;

            return redDominant || blueDominant;
        }

        /// <summary>
        /// How colourful a colour is, from 0 for grey to 1 for fully saturated.
        /// </summary>
        private static double Saturation(double r, double g, double b)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));

            if (max <= 0)
            {
                return 0;
            }

            return (max - min) / max;
        }

        /// <summary>
        /// Average brightness of the block a pixel belongs to.
        /// </summary>
        private static double Average(double[] blockMean, int blocksX, int blockSize, int x, int y)
        {
            int index = ((y / blockSize) * blocksX + (x / blockSize)) * 3;
            return (blockMean[index] + blockMean[index + 1] + blockMean[index + 2]) / 3.0;
        }

        /// <summary>
        /// Adds the pixels that are close to a match, so that a shape which the colour
        /// test broke into pieces is treated as one shape again.
        /// </summary>
        private void ConnectNearbyMatches(List<Point> matches, int width, int height, int radius)
        {
            if (radius <= 0 || matches.Count == 0)
            {
                return;
            }

            int lookupSize = width * height;
            if (lookupBuffer == null || lookupBuffer.Length < lookupSize)
            {
                lookupBuffer = new int[lookupSize];
                visitedBuffer = new bool[lookupSize];
            }

            int[] lookup = lookupBuffer!;
            bool[] visited = visitedBuffer!;

            for (int i = 0; i < lookupSize; i++)
            {
                lookup[i] = -1;
                visited[i] = false;
            }

            foreach (var m in matches)
            {
                lookup[m.Y * width + m.X] = 1;
            }

            var added = new List<Point>();

            foreach (var m in matches)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int nx = m.X + dx;
                        int ny = m.Y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        {
                            continue;
                        }

                        int index = ny * width + nx;
                        if (lookup[index] >= 0 || visited[index])
                        {
                            continue;
                        }

                        visited[index] = true;
                        added.Add(new Point(nx, ny));
                    }
                }
            }

            matches.AddRange(added);
        }

        /// <summary>
        /// Groups the matching pixels into connected regions, so that a bobber can be told
        /// apart from scattered background.
        /// </summary>
        private List<BobberCandidate> GroupIntoCandidates(List<Point> matches, int width, int height)
        {
            int lookupSize = width * height;
            if (lookupBuffer == null || lookupBuffer.Length < lookupSize)
            {
                lookupBuffer = new int[lookupSize];
                visitedBuffer = new bool[lookupSize];
            }

            int[] lookup = lookupBuffer!;
            bool[] visited = visitedBuffer!;

            for (int i = 0; i < lookupSize; i++)
            {
                lookup[i] = -1;
                visited[i] = false;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                lookup[matches[i].Y * width + matches[i].X] = i;
            }

            var candidates = new List<BobberCandidate>();
            stack.Clear();

            for (int i = 0; i < matches.Count; i++)
            {
                if (visited[matches[i].Y * width + matches[i].X])
                {
                    continue;
                }

                visited[matches[i].Y * width + matches[i].X] = true;
                stack.Push(i);

                var candidate = new BobberCandidate
                {
                    TopLeft = matches[i],
                    BottomRight = matches[i]
                };

                int sumX = 0;
                int sumY = 0;
                int corePixels = 0;

                while (stack.Count > 0)
                {
                    int current = stack.Pop();
                    Point p = matches[current];

                    candidate.Area++;
                    sumX += p.X;
                    sumY += p.Y;

                    // Count the pixels that have matching pixels on all four sides. These
                    // are the pixels that survive eroding the shape by one pixel, which
                    // is what tells a filled shape apart from a thin curve.
                    if (IsCorePixel(p, lookup, width, height))
                    {
                        corePixels++;
                    }

                    if (p.X < candidate.TopLeft.X) candidate.TopLeft = new Point(p.X, candidate.TopLeft.Y);
                    if (p.Y < candidate.TopLeft.Y) candidate.TopLeft = new Point(candidate.TopLeft.X, p.Y);
                    if (p.X > candidate.BottomRight.X) candidate.BottomRight = new Point(p.X, candidate.BottomRight.Y);
                    if (p.Y > candidate.BottomRight.Y) candidate.BottomRight = new Point(candidate.BottomRight.X, p.Y);

                    // 8 neighbours, so a diagonal run of pixels stays connected.
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0)
                            {
                                continue;
                            }

                            int nx = p.X + dx;
                            int ny = p.Y + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                            {
                                continue;
                            }

                            int neighbour = lookup[ny * width + nx];
                            if (neighbour < 0 || visited[ny * width + nx])
                            {
                                continue;
                            }

                            visited[ny * width + nx] = true;
                            stack.Push(neighbour);
                        }
                    }
                }

                candidate.Centre = new Point(sumX / candidate.Area, sumY / candidate.Area);
                candidate.Solidity = (double)corePixels / candidate.Area;
                candidates.Add(candidate);
            }

            return candidates;
        }

        /// <summary>
        /// True when the pixel has matching pixels on all four sides, which means it
        /// survives eroding the shape by one pixel.
        /// </summary>
        private static bool IsCorePixel(Point p, int[] lookup, int width, int height)
        {
            return HasMatch(p.X - 1, p.Y, lookup, width, height)
                && HasMatch(p.X + 1, p.Y, lookup, width, height)
                && HasMatch(p.X, p.Y - 1, lookup, width, height)
                && HasMatch(p.X, p.Y + 1, lookup, width, height);
        }

        private static bool HasMatch(int x, int y, int[] lookup, int width, int height)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return false;
            }

            return lookup[y * width + x] >= 0;
        }

        /// <summary>
        /// Picks the candidate that looks most like a bobber.
        /// </summary>
        private BobberCandidate? SelectBest(List<BobberCandidate> candidates, Point? previousLocation, Point frameCentre, int frameWidth, int frameHeight)
        {
            BobberCandidate? best = null;
            int bestScore = int.MinValue;

            foreach (var candidate in candidates)
            {
                if (!LooksLikeBobber(candidate, frameWidth, frameHeight))
                {
                    continue;
                }

                int score = Score(candidate, previousLocation, frameCentre, frameWidth);
                if (score == int.MinValue)
                {
                    // This candidate cannot be the bobber, because it is too far from
                    // where the bobber was last seen.
                    continue;
                }

                if (best == null || score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// True when the candidate has the size and shape of a fishing bobber rather than
        /// a patch of red landscape.
        /// 
        /// The size limits are worked out from the size of the frame, because the
        /// bobber covers a different number of pixels on every screen.
        /// </summary>
        private bool LooksLikeBobber(BobberCandidate candidate, int frameWidth, int frameHeight)
        {
            int minArea = (int)(frameWidth * frameHeight * MinAreaFraction);
            int maxArea = (int)(frameWidth * frameHeight * MaxAreaFraction);
            int maxWidth = (int)(frameWidth * MaxWidthFraction);
            int maxHeight = (int)(frameHeight * MaxHeightFraction);

            if (candidate.Area < minArea || candidate.Area > maxArea)
            {
                return false;
            }

            if (candidate.Width > maxWidth || candidate.Height > maxHeight)
            {
                return false;
            }

            if (candidate.FillRatio < MinFillRatio)
            {
                return false;
            }

            if (candidate.Solidity < MinSolidity)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Ranks a candidate. A bigger group is more likely to be the bobber, and one
        /// that is near the previous position or near the middle of the frame is more
        /// likely still.
        /// </summary>
        private int Score(BobberCandidate candidate, Point? previousLocation, Point frameCentre, int frameWidth)
        {
            int score = candidate.Area / 10;

            if (previousLocation.HasValue)
            {
                int dx = Math.Abs(candidate.Centre.X - previousLocation.Value.X);
                int dy = Math.Abs(candidate.Centre.Y - previousLocation.Value.Y);
                int distance = Math.Max(dx, dy);

                if (distance > SearchRadius)
                {
                    // Too far from where the bobber was last seen to be the same bobber.
                    return int.MinValue;
                }

                // Closer is better.
                score += SearchRadius - distance;
            }

            // The float is cast in front of the character, so it is expected to be in
            // the middle of the frame. A patch of red terrain is usually somewhere else.
            int centreDx = Math.Abs(candidate.Centre.X - frameCentre.X);
            int centreDy = Math.Abs(candidate.Centre.Y - frameCentre.Y);
            int centreDistance = Math.Max(centreDx, centreDy);

            int maxCentreDistance = (int)(frameWidth * MaxCentreDistanceFraction);
            if (centreDistance > maxCentreDistance)
            {
                // Too far from the middle of the screen to be the float.
                return int.MinValue;
            }

            score += CentreBias - centreDistance;

            return score;
        }
    }
}
