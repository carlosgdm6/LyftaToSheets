using System.Text.Json.Serialization;

namespace LyftaToSheets.Lyfta;

public sealed class LyftaConfig
{
    public string BaseUrl { get; set; } = "https://my.lyfta.app";
    public string Token { get; set; } = "";
    public string AuthScheme { get; set; } = "Bearer";
}

// ---- Modelos da resposta (só os campos que usamos) ----

public sealed class LyftaWorkoutsResponse
{
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
    [JsonPropertyName("current_page")] public int CurrentPage { get; set; }
    public List<LyftaWorkout>? Workouts { get; set; }
}

public sealed class LyftaWorkout
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    [JsonPropertyName("workout_perform_date")] public string WorkoutPerformDate { get; set; } = "";
    public List<LyftaExercise>? Exercises { get; set; }
}

public sealed class LyftaExercise
{
    [JsonPropertyName("excercise_name")] public string ExerciseName { get; set; } = "";
    public List<LyftaSet>? Sets { get; set; }
}

public sealed class LyftaSet
{
    public string Weight { get; set; } = "";
    public string Reps { get; set; } = "";
}
