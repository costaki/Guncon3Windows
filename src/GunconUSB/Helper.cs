namespace GunconUSB
{
    public static class Helper
    {
        public const int GunAxisMin = 0;
        public const int GunAxisMax = 32767;

        // Maps a value from one range into another (used by AbsMouseFeeder for 4:3)
        public static int ConvertRange(int inMin, int inMax, int outMin, int outMax, int value)
        {
            if (inMax == inMin) return outMin;
            long num = (long)(value - inMin) * (outMax - outMin);
            return outMin + (int)(num / (inMax - inMin));
        }

        public static int ConvertRange4By3(short value)
        {
            // Map full-width logical X (0..32767) into the centered 4:3 region of a 16:9 screen.
            // For 16:9, the 4:3 region uses 3/4 of the screen width (since (4/3)/(16/9)=3/4),
            // leaving 1/8 margin on each side.
            const int inMin = GunAxisMin;
            const int inMax = GunAxisMax;
            const int margin = 4096; // 32768 * 1/8
            const int outMin = margin;
            const int outMax = GunAxisMax - margin;
            return ConvertRange(inMin, inMax, outMin, outMax, (int)value);
        }

        public static bool IsInsideCentered4By3(short value)
        {
            const int margin = 4096; // 32768 * 1/8
            return value >= margin && value <= (GunAxisMax - margin);
        }
    }
}
