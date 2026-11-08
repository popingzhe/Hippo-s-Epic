// 本文件由 Tools/Map/收集所有场景数据 自动生成，请勿手动修改
using System.Collections.Generic;

public enum SceneMapId
{
    // --- Scene_Doomed_Hospital ---
    BodyHouse, // Body_house
    InnerHospital, // Inner_hospital
    OutHospital, // Out_hospital

    // --- Scene_Outer_City ---
    BehindMoon, // Behind_Moon
    OuterCityGround, // Outer_City_Ground

    // --- Scene_Punk_City ---
    PunkCityGround, // Punk_City_Ground
    PunkCityMovie, // Punk_City_movie

    // --- Scene_Sewer ---
    SewerSurface, // Sewer_Surface

    // --- Scene_Staff_Lounge ---
    OutSideGym, // Out_Side_Gym
    StaffLounge, // Staff Lounge
    UndergroundBoxingGym, // Underground Boxing Gym

    // --- Temple ---
    Temple, // temple
}

public static class SceneMapIdExtensions
{
    private static readonly Dictionary<SceneMapId, string> MapNames = new Dictionary<SceneMapId, string>
    {
        { SceneMapId.BodyHouse, "Body_house" },
        { SceneMapId.InnerHospital, "Inner_hospital" },
        { SceneMapId.OutHospital, "Out_hospital" },
        { SceneMapId.BehindMoon, "Behind_Moon" },
        { SceneMapId.OuterCityGround, "Outer_City_Ground" },
        { SceneMapId.PunkCityGround, "Punk_City_Ground" },
        { SceneMapId.PunkCityMovie, "Punk_City_movie" },
        { SceneMapId.SewerSurface, "Sewer_Surface" },
        { SceneMapId.OutSideGym, "Out_Side_Gym" },
        { SceneMapId.StaffLounge, "Staff Lounge" },
        { SceneMapId.UndergroundBoxingGym, "Underground Boxing Gym" },
        { SceneMapId.Temple, "temple" },
    };

    private static readonly Dictionary<SceneMapId, string> SceneNames = new Dictionary<SceneMapId, string>
    {
        { SceneMapId.BodyHouse, "Scene_Doomed_Hospital" },
        { SceneMapId.InnerHospital, "Scene_Doomed_Hospital" },
        { SceneMapId.OutHospital, "Scene_Doomed_Hospital" },
        { SceneMapId.BehindMoon, "Scene_Outer_City" },
        { SceneMapId.OuterCityGround, "Scene_Outer_City" },
        { SceneMapId.PunkCityGround, "Scene_Punk_City" },
        { SceneMapId.PunkCityMovie, "Scene_Punk_City" },
        { SceneMapId.SewerSurface, "Scene_Sewer" },
        { SceneMapId.OutSideGym, "Scene_Staff_Lounge" },
        { SceneMapId.StaffLounge, "Scene_Staff_Lounge" },
        { SceneMapId.UndergroundBoxingGym, "Scene_Staff_Lounge" },
        { SceneMapId.Temple, "Temple" },
    };

    private static readonly Dictionary<string, SceneMapId> MapNameToId = new Dictionary<string, SceneMapId>
    {
        { "Body_house", SceneMapId.BodyHouse },
        { "Inner_hospital", SceneMapId.InnerHospital },
        { "Out_hospital", SceneMapId.OutHospital },
        { "Behind_Moon", SceneMapId.BehindMoon },
        { "Outer_City_Ground", SceneMapId.OuterCityGround },
        { "Punk_City_Ground", SceneMapId.PunkCityGround },
        { "Punk_City_movie", SceneMapId.PunkCityMovie },
        { "Sewer_Surface", SceneMapId.SewerSurface },
        { "Out_Side_Gym", SceneMapId.OutSideGym },
        { "Staff Lounge", SceneMapId.StaffLounge },
        { "Underground Boxing Gym", SceneMapId.UndergroundBoxingGym },
        { "temple", SceneMapId.Temple },
    };

    public static string GetMapName(this SceneMapId id) => MapNames[id];
    public static string GetSceneName(this SceneMapId id) => SceneNames[id];
    public static SceneMapId GetSceneMapIdByMapName(string mapName) => MapNameToId.TryGetValue(mapName, out var id) ? id : SceneMapId.StaffLounge;
    public static IEnumerable<string> GetAllMapNames() => MapNameToId.Keys;
}
