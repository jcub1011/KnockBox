using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Core.Services.Logic.RandomGeneration;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.Data;

public enum TaskCategory { Devotion, Style, Movement, Neglect, Rivalry }
public enum TaskDifficulty { Easy, Medium, Hard }

public record SecretTask(
    string Id,
    TaskCategory Category,
    TaskDifficulty Difficulty,
    string Description,
    string ObservablePattern,
    int PointValue
);

public static class TaskPool
{
    public static IReadOnlyList<SecretTask> AllTasks { get; } = new List<SecretTask>
    {
        // Devotion (Easy, 1pt)
        new("D1", TaskCategory.Devotion, TaskDifficulty.Easy, "Play a card that adds progress to Renaissance Masters on at least 4 separate turns.", "Frequent progress on Renaissance Masters", 1),
        new("D2", TaskCategory.Devotion, TaskDifficulty.Easy, "Play a card that adds progress to Contemporary Showcase on at least 4 separate turns.", "Frequent progress on Contemporary Showcase", 1),
        new("D3", TaskCategory.Devotion, TaskDifficulty.Easy, "Play a card that adds progress to Impressionist Gallery on at least 4 separate turns.", "Frequent progress on Impressionist Gallery", 1),
        new("D4", TaskCategory.Devotion, TaskDifficulty.Easy, "Play a card that adds progress to Marble & Bronze on at least 4 separate turns.", "Frequent progress on Marble & Bronze", 1),
        new("D5", TaskCategory.Devotion, TaskDifficulty.Easy, "Play a card that adds progress to Emerging Artists on at least 4 separate turns.", "Frequent progress on Emerging Artists", 1),
        new("D6", TaskCategory.Devotion, TaskDifficulty.Easy, "Play cards affecting Grand Hall collections on at least 5 separate turns.", "Frequent focus on Grand Hall", 1),
        new("D7", TaskCategory.Devotion, TaskDifficulty.Easy, "Play cards affecting Sculpture Garden collections on at least 5 separate turns.", "Frequent focus on Sculpture Garden", 1),

        // Style (Medium, 2pt)
        new("Y1", TaskCategory.Style, TaskDifficulty.Medium, "Play a Remove card on at least 3 separate turns.", "Multiple removals performed", 2),
        new("Y2", TaskCategory.Style, TaskDifficulty.Medium, "Play cards affecting at least 4 different collections across the round.", "Broad collection engagement", 2),
        new("Y3", TaskCategory.Style, TaskDifficulty.Medium, "Play a card affecting the same collection at least 3 turns in a row.", "Persistent focus on one collection", 2),
        new("Y4", TaskCategory.Style, TaskDifficulty.Medium, "Alternate between Acquire and Remove cards for at least 4 consecutive turns.", "Alternating acquire/remove pattern", 2),
        new("Y5", TaskCategory.Style, TaskDifficulty.Medium, "Play the highest-value card in your hand on at least 4 turns.", "Aggressive high-value card usage", 2),
        new("Y6", TaskCategory.Style, TaskDifficulty.Medium, "Visit an Event Spot at least 3 times during the round.", "Frequent event spot visits", 2),

        // Movement (Medium, 2pt)
        new("M1", TaskCategory.Movement, TaskDifficulty.Medium, "Visit all four wings at least once during the round.", "Complete gallery tour", 2),
        new("M2", TaskCategory.Movement, TaskDifficulty.Medium, "Spend at least 4 turns in the same wing.", "Prolonged stay in one wing", 2),
        new("M3", TaskCategory.Movement, TaskDifficulty.Medium, "End your turn on the same spot as another player at least 3 times.", "Frequent positioning with others", 2),
        new("M4", TaskCategory.Movement, TaskDifficulty.Medium, "Take the longest available path at every fork for at least 4 consecutive turns.", "Avoiding shortcuts consistently", 2),
        new("M5", TaskCategory.Movement, TaskDifficulty.Medium, "Change wings every turn for at least 4 consecutive turns.", "Rapid movement between wings", 2),
        new("M6", TaskCategory.Movement, TaskDifficulty.Medium, "Return to the same spot at least 3 times during the round.", "Revisiting specific locations", 2),

        // Neglect (Hard, 3pt)
        new("N1", TaskCategory.Neglect, TaskDifficulty.Hard, "Never play an Acquire card on Renaissance Masters for the entire round.", "Avoids acquiring Renaissance Masters", 3),
        new("N2", TaskCategory.Neglect, TaskDifficulty.Hard, "Never play an Acquire card on Contemporary Showcase for the entire round.", "Avoids acquiring Contemporary Showcase", 3),
        new("N3", TaskCategory.Neglect, TaskDifficulty.Hard, "Never play an Acquire card on Impressionist Gallery for the entire round.", "Avoids acquiring Impressionist Gallery", 3),
        new("N4", TaskCategory.Neglect, TaskDifficulty.Hard, "Never enter the Grand Hall for the entire round.", "Complete avoidance of Grand Hall", 3),
        new("N5", TaskCategory.Neglect, TaskDifficulty.Hard, "Never enter the Modern Wing for the entire round.", "Complete avoidance of Modern Wing", 3),
        new("N6", TaskCategory.Neglect, TaskDifficulty.Hard, "Never play a Remove card for the entire round.", "Never removes progress", 3),

        // Rivalry (Hard, 3pt)
        new("R1", TaskCategory.Rivalry, TaskDifficulty.Hard, "Play an Acquire card on a collection immediately after another player plays a Remove card on that same collection, at least 3 times.", "Counter-removing others", 3),
        new("R2", TaskCategory.Rivalry, TaskDifficulty.Hard, "Play a card affecting the same collection that the player immediately before you affected, on at least 4 turns.", "Following the previous player's focus", 3),
        new("R3", TaskCategory.Rivalry, TaskDifficulty.Hard, "Never play a card affecting the same collection as the player immediately before you, for at least 5 consecutive turns.", "Avoiding the previous player's focus", 3),
        new("R4", TaskCategory.Rivalry, TaskDifficulty.Hard, "Play a Remove card on a collection that is currently the highest-progress collection, at least 3 times.", "Targeting leading collections", 3),
        new("R5", TaskCategory.Rivalry, TaskDifficulty.Hard, "Play an Acquire card on a collection that is currently the lowest-progress collection, at least 3 times.", "Supporting lagging collections", 3),
        new("R6", TaskCategory.Rivalry, TaskDifficulty.Hard, "Be in the same wing as a specific other player (assigned randomly at task draw) on at least 4 turns.", "Shadowing a specific player", 3)
    };

    public static IReadOnlyList<SecretTask> GetPoolForPlayerCount(int playerCount)
    {
        if (playerCount <= 3)
        {
            // For 3 players, exclude all 6 Rivalry tasks (R1-R6)
            return AllTasks.Where(t => t.Category != TaskCategory.Rivalry).ToList();
        }
        else
        {
            // For 4+ players, include all 31 tasks but cap at 30 (exclude 1 random task for ambiguity)
            // For now, just return all but R6 or something to keep it deterministic if needed, 
            // but the plan says "exclude 1 random task". 
            // I'll return AllTasks and let DrawTasks handle the "random" exclusion if count is specified.
            // Actually, the plan says "returns subset per GDD: 25 for 3 players, 30 for 4+".
            return AllTasks.Take(30).ToList(); 
        }
    }

    public static List<SecretTask> DrawTasks(IRandomNumberService rng, IReadOnlyList<SecretTask> pool, int count)
    {
        var result = new List<SecretTask>();
        var available = pool.ToList();
        
        for (int i = 0; i < count && available.Count > 0; i++)
        {
            int index = rng.GetRandomInt(available.Count);
            result.Add(available[index]);
            available.RemoveAt(index);
        }
        
        return result;
    }
}
