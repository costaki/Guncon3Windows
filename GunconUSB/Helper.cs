namespace GunconUSB
{
    public static class Helper
    {
        // Mapea un rango a otro (lo usa AbsMouseFeeder para el 4:3)
        public static int ConvertRange(int inMin, int inMax, int outMin, int outMax, int value)
        {
            if (inMax == inMin) return outMin;
            long num = (long)(value - inMin) * (outMax - outMin);
            return outMin + (int)(num / (inMax - inMin));
        }
    }
}
