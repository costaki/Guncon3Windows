namespace GunconUSB
{
    public static class Helper
    {
        // Maps a value from one range into another (used by AbsMouseFeeder for 4:3)
        public static int ConvertRange(int inMin, int inMax, int outMin, int outMax, int value)
        {
            if (inMax == inMin) return outMin;
            long num = (long)(value - inMin) * (outMax - outMin);
            return outMin + (int)(num / (inMax - inMin));
        }

        public static int ConvertRange4By3(short value)
        {
            const int inMin = 4096;
            const int inMax = 28671;
            const int outMin = 0;
            const int outMax = 32767;
            return ConvertRange(inMin, inMax, outMin, outMax, (int)value);
        }
    }
}
