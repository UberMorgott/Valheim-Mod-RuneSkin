namespace RuneSkin.Theme
{
    // Which theme face a legacy text (uGUI Text, IMGUI style) takes. Our faces are single weights (PT Sans Regular body,
    // Cormorant Bold display, the bold rune face), so a Bold style never stays on them (Unity would smear a faux bold):
    // bold body text takes the display face, which is a real bold cut; every other face keeps its token.
    public static class LegacyFaces
    {
        public const string Body = "font.body";
        public const string Display = "font.display";

        public static string FaceToken(string token, bool bold) => bold && token == Body ? Display : token;
    }
}
