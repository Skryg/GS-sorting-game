// Third-party assets used in the game. Keep in sync with the "Credits" section of README.md.
public static class CreditsText
{
    private const string Head = "<color=#5FD3E6><b>";
    private const string HeadEnd = "</b></color>";
    private const string Dim = "<color=#9AA4B2>";
    private const string DimEnd = "</color>";

    public static readonly string LeftColumn =
        Head + "3D models (Sketchfab, CC BY 4.0)" + HeadEnd + "\n" +
        "Apple - Moshe Caine\n" +
        "Orange fruit - polyplant3d\n" +
        "Tools / Monkey Tool - ROY\n" +
        "Digital Radio Clock - NoMoreFeelings\n" +
        "Electronic Calculator Citizen - Dokazvo\n" +
        "Decretive Medieval Plate - wolfgar74\n" +
        "Sci-Fi Fusion Plasma Gun - Abdooelsabry\n" +
        Dim + "Turned into Gaussian splats for this game" + DimEnd + "\n\n" +
        Head + "Unity Asset Store" + HeadEnd + "\n" +
        "Fantasy Spider\n" +
        "Sci-Fi Construction Kit (Modular)\n\n" +
        Head + "Icons" + HeadEnd + "\n" +
        "OpenMoji - CC BY-SA 4.0";

    public static readonly string RightColumn =
        Head + "Sounds and music (CC0)" + HeadEnd + "\n" +
        "Kenney - Interface Sounds, Sci-fi Sounds,\nMusic Jingles\n" +
        "Conveyor belt - TiesWijnen (Freesound)\n" +
        "80 CC0 creature SFX #1, #2 - rubberduck\n" +
        "Monster Sound Effects Pack - Ogrebane\n" +
        "Dark Sci-Fi Audio Pack - SRG774\n" +
        "37 hits/punches - Independent.nu\n" +
        Dim + "OpenGameArt.org" + DimEnd + "\n" +
        Dim + "Crumble and bomb sounds made for this game" + DimEnd + "\n\n" +
        Head + "Technology" + HeadEnd + "\n" +
        "Unity Gaussian Splatting - Aras Pranckevicius (MIT)\n" +
        "GS-Verse - mesh-based Gaussian splat interaction\n" +
        "GaMeS - Gaussian Mesh Splatting\n" +
        "PlyImporter - 3DBear (MIT)\n" +
        "TRELLIS - image to 3D generation\n" +
        "XR Interaction Toolkit - Unity";
}
