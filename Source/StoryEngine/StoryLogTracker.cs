using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Verse;
using RimWorld;

namespace AIPortraits.StoryEngine
{
    public static class StoryLogTracker
    {
        // Buffer to hold raw logs for the current interval
        private static StringBuilder dailyLogBuffer = new StringBuilder();
        
        // Caches RimLog last read lines to avoid duplicates
        private static int rimLogLastLineRead = 0;
        
        // Caches IDs of already processed log entries
        private static HashSet<LogEntry> processedPlayLogIds = new HashSet<LogEntry>();
        private static HashSet<LogEntry> processedBattleLogIds = new HashSet<LogEntry>();
        private static HashSet<int> processedTaleIds = new HashSet<int>();
        private static bool initialized = false;

        public static void InitializeIfNecessary()
        {
            if (initialized) return;
            initialized = true;

            // Populate the processed sets with existing log entries to avoid logging historic events on game load
            if (Find.PlayLog != null)
            {
                foreach (var entry in Find.PlayLog.AllEntries)
                {
                    processedPlayLogIds.Add(entry);
                }
            }

            if (Find.BattleLog != null)
            {
                foreach (var battle in Find.BattleLog.Battles)
                {
                    foreach (var entry in battle.Entries)
                    {
                        processedBattleLogIds.Add(entry);
                    }
                }
            }

            if (Find.TaleManager != null)
            {
                foreach (var tale in Find.TaleManager.AllTalesListForReading)
                {
                    processedTaleIds.Add(tale.id);
                }
            }
        }

        public static void AddLogEvent(string logEvent)
        {
            if (AIPortraitsMod.settings == null || !AIPortraitsMod.settings.enableStoryEngine) return;
            
            Map map = Find.CurrentMap;
            if (map == null && Find.Maps.Count > 0) map = Find.Maps[0];
            string timestamp = (map != null) ? GenLocalDate.HourOfDay(map) + ":00 - " : "";
            dailyLogBuffer.AppendLine(timestamp + logEvent);
        }

        public static void PollNativeLogs()
        {
            InitializeIfNecessary();

            if (Find.PlayLog != null)
            {
                foreach (var entry in Find.PlayLog.AllEntries)
                {
                    if (!processedPlayLogIds.Contains(entry))
                    {
                        AddLogEvent(string.Format("Interaction/Event: {0}", entry.ToGameStringFromPOV(null, false)));
                        processedPlayLogIds.Add(entry);
                    }
                }
            }

            if (Find.BattleLog != null)
            {
                foreach (var battle in Find.BattleLog.Battles)
                {
                    foreach (var entry in battle.Entries)
                    {
                        if (!processedBattleLogIds.Contains(entry))
                        {
                            AddLogEvent(string.Format("Combat: {0}", entry.ToGameStringFromPOV(null, false)));
                            processedBattleLogIds.Add(entry);
                        }
                    }
                }
            }

            if (Find.TaleManager != null)
            {
                foreach (var tale in Find.TaleManager.AllTalesListForReading)
                {
                    if (!processedTaleIds.Contains(tale.id))
                    {
                        AddLogEvent(string.Format("Tale (Major Event): {0}", tale.ToString()));
                        processedTaleIds.Add(tale.id);
                    }
                }
            }
        }

        public static void PollExternalLogs()
        {
            // Specifically look for RimLog files on desktop if applicable
            string docsFallback = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
            string rimLogPath = Path.Combine(docsFallback, "rimlog.txt");
            
            if (File.Exists(rimLogPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(rimLogPath);
                    if (lines.Length > rimLogLastLineRead)
                    {
                        for (int i = rimLogLastLineRead; i < lines.Length; i++)
                        {
                            AddLogEvent(string.Format("[RimLog] {0}", lines[i]));
                        }
                        rimLogLastLineRead = lines.Length;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("[Story Engine] Failed to read external RimLog: " + ex.Message);
                }
            }
        }

        public static string GetAndClearBuffer()
        {
            string logs = dailyLogBuffer.ToString();
            dailyLogBuffer.Clear();
            return logs;
        }
    }
}
