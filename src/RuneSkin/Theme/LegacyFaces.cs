namespace RuneSkin.Theme
{
    // Which theme face a legacy text (uGUI Text, IMGUI style) takes. The theme has two faces (user 2026-10-22): PT Sans
    // (body) and the rune face Valheim-Norsebold (display). Body legacy texts take the bundle's dynamic PT Sans (theme
    // "legacy"), display ones the rune face's own source font. A Bold style never stays as a style (Unity would smear a faux bold): bold body text takes PT Sans Bold.
    public static class LegacyFaces
    {
        public const string Body = "font.body";
        public const string Display = "font.display";

        public const string BodyBold = "font.body.bold";

        // Bold body text takes the real bold cut (PT Sans Bold); every other face keeps its token.
        public static string FaceToken(string token, bool bold) => bold && token == Body ? BodyBold : token;
    }
}
