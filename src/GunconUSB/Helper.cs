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
    }
}
