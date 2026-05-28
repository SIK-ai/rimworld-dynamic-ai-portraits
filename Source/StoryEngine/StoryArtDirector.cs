using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;
using RimWorld;

namespace AIPortraits.StoryEngine
{
    public static class StoryArtDirector
    {
        public static void ProcessStoryResponse(string jsonResponse)
        {
            try
            {
                // Basic JSON parsing (using string splitting/Regex to avoid adding large JSON deps to C# 5)
                string novelText = ExtractJsonValue(jsonResponse, "novel_chapter");
                
                string docsDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
                string storyDir = Path.Combine(docsDir, "RimWorld Portraits", "Storybooks");
                if (!Directory.Exists(storyDir)) Directory.CreateDirectory(storyDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string storyFile = Path.Combine(storyDir, "Story_" + timestamp + ".md");

                Vector2 longLat = Vector2.zero;
                if (Find.CurrentMap != null)
                {
                    longLat = Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile);
                }
                else if (Find.Maps.Count > 0)
                {
                    longLat = Find.WorldGrid.LongLatOf(Find.Maps[0].Tile);
                }

                string dateStr = GenDate.DateFullStringAt(GenTicks.TicksAbs, longLat);

                using (StreamWriter writer = new StreamWriter(storyFile, true))
                {
                    writer.WriteLine("# RimWorld Chronicle - " + dateStr);
                    writer.WriteLine();
                    writer.WriteLine(novelText);
                    writer.WriteLine();
                }

                if (AIPortraitsMod.settings.storyIncludeComicPanels)
                {
                    GenerateComicPanels(jsonResponse, storyFile);
                }

                Log.Message("[Story Engine] Story chapter written to: " + storyFile);
            }
            catch (Exception ex)
            {
                Log.Error("[Story Engine] Failed to process story response: " + ex.Message);
            }
        }

        private static void GenerateComicPanels(string json, string storyFile)
        {
            // Simple extraction of panels array
            int panelsStart = json.IndexOf("\"comic_panels\"");
            if (panelsStart == -1) return;

            MatchCollection matches = Regex.Matches(json, @"\""scene_description\""\s*:\s*\""(.*?)\""", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            
            int panelIndex = 1;
            string storyDir = Path.GetDirectoryName(storyFile);

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    string scene = match.Groups[1].Value.Replace("\\n", " ").Replace("\\\"", "\"");
                    string fullPrompt = scene + ", comic book panel, graphic novel illustration, " + AIPortraitsMod.settings.manhwaStylePrompt;

                    // Queue the image generation
                    string panelId = "Panel_" + panelIndex;
                    Log.Message("[Story Engine] Queueing Panel: " + fullPrompt);
                    
                    // We don't have a state for the panel, so we pass a dummy or null state
                    // The AsyncAIClient queueing handles the API call
                    AsyncAIClient.QueueCustomGeneration(fullPrompt, AIPortraitsMod.settings, null, null, (tex, bytes, prompt, err) => 
                    {
                        if (bytes != null && err == null)
                        {
                            string imgPath = storyFile.Replace(".md", "_" + panelId + ".png");
                            File.WriteAllBytes(imgPath, bytes);

                            // Append to story file
                            File.AppendAllText(storyFile, string.Format("\n![{0}]({1})\n", panelId, Path.GetFileName(imgPath)));
                        }
                    });
                    panelIndex++;
                }
            }
        }

        private static string ExtractJsonValue(string json, string key)
        {
            string pattern = string.Format("\"{0}\"\\s*:\\s*\"(.*?)\"", key);
            Match match = Regex.Match(json, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value.Replace("\\n", "\n").Replace("\\\"", "\"");
            }
            return "";
        }
    }
}
