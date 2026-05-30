using System;
using Verse;
using RimWorld;
using UnityEngine;

namespace AIPortraits.StoryEngine
{
    public class StoryOrchestrator : GameComponent
    {
        private int lastGenerationDay = -1;

        public StoryOrchestrator(Game game)
        {
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            
            if (AIPortraitsMod.settings == null || !AIPortraitsMod.settings.enableStoryEngine)
                return;

            // Stagger polling across different ticks to avoid CPU spikes during tick loops
            if (Find.TickManager.TicksGame % 2000 == 0)
            {
                StoryLogTracker.PollNativeLogs();
            }
            else if (Find.TickManager.TicksGame % 2000 == 1000)
            {
                StoryLogTracker.PollExternalLogs();
            }

            // Check if we should generate the story (e.g. at midnight)
            if (Find.TickManager.TicksGame % 60000 == 0) // Once per in-game day (60k ticks)
            {
                Map map = Find.CurrentMap;
                if (map == null && Find.Maps.Count > 0) map = Find.Maps[0];
                if (map == null) return;

                int currentDay = GenLocalDate.DayOfYear(map);
                int interval = AIPortraitsMod.settings.storyGenerationIntervalDays;

                if (lastGenerationDay == -1) 
                    lastGenerationDay = currentDay; // Initialize on first day

                if (Mathf.Abs(currentDay - lastGenerationDay) >= interval)
                {
                    GenerateStory();
                    lastGenerationDay = currentDay;
                }
            }
        }

        private void GenerateStory()
        {
            string logs = StoryLogTracker.GetAndClearBuffer();
            if (string.IsNullOrEmpty(logs)) return;

            string systemPrompt = @"You are a Sci-Fi Novelist and Comic Script Writer.
You are chronicling the history of a RimWorld colony.
Read the provided game logs.
Output a valid JSON object with the following schema:
{
  ""novel_chapter"": ""A highly descriptive narrative chapter covering these events."",
  ""comic_panels"": [
    {
      ""scene_description"": ""Visual description of the scene."",
      ""pawn_names"": [""List of character names in the scene""],
      ""dialogue"": ""Text for a speech bubble, if any""
    }
  ]
}";
            string finalPrompt = systemPrompt + "\n\nLOGS:\n" + logs;
            
            Log.Message("[Story Engine] Triggering LLM Story Generation for " + logs.Length + " chars of log.");

            // Use the LLM integration in AsyncAIClient
            AsyncAIClient.QueueLLMPrompt(finalPrompt, AIPortraitsMod.settings.storyLlmModel, AIPortraitsMod.settings.llmApiKey, (response, err) =>
            {
                if (!string.IsNullOrEmpty(err))
                {
                    Log.Error("[Story Engine] Failed to generate story: " + err);
                    return;
                }

                StoryArtDirector.ProcessStoryResponse(response);
            });
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastGenerationDay, "lastGenerationDay", -1);
        }
    }
}
