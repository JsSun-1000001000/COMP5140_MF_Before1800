using UnityEngine;

namespace Before1800.PartB
{
    public static class PlayerUIFont
    {
        private static Font font;

        public static Font Get()
        {
            if (!font) font = Resources.Load<Font>("SourceHanSansCN-Regular");
            return font;
        }
    }
}
