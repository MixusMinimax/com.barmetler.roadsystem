namespace Barmetler.Util
{
    public static class Functional
    {
        public static T Let<T>(T value, out T result) where T : class
        {
            result = value;
            return value;
        }
    }
}