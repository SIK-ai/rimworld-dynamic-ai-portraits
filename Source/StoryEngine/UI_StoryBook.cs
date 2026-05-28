using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace AIPortraits.StoryEngine
{
    public class UI_StoryBook : Window
    {
        private Vector2 scrollPos;
        private string rawStoryText = "";
        private string storyDir = "";
        
        private struct ContentElement
        {
            public bool isImage;
            public string text;
            public string imagePath;
            public Texture2D texture;
            public string missingText;
        }

        private List<ContentElement> elements = new List<ContentElement>();
        private Dictionary<string, Texture2D> loadedTextures = new Dictionary<string, Texture2D>();

        private string titleText;
        private string openFolderText;
        private string reloadStoryText;

        public override Vector2 InitialSize { get { return new Vector2(850f, 650f); } }

        public UI_StoryBook()
        {
            this.doCloseX = true;
            this.forcePause = false;
            this.absorbInputAroundWindow = false;
            
            titleText = "DAP_StoryBook_Title".Translate();
            openFolderText = "DAP_StoryBook_OpenFolder".Translate();
            reloadStoryText = "DAP_StoryBook_ReloadStory".Translate();

            string docsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            storyDir = Path.Combine(docsDir, "RimWorld Portraits", "Storybooks");
            
            LoadLatestStory();
        }

        private void LoadLatestStory()
        {
            elements.Clear();
            
            if (!Directory.Exists(storyDir))
            {
                rawStoryText = "DAP_StoryBook_NoStories".Translate();
                elements.Add(new ContentElement { isImage = false, text = rawStoryText });
                return;
            }

            string[] files = Directory.GetFiles(storyDir, "*.md");
            if (files.Length == 0)
            {
                rawStoryText = "DAP_StoryBook_NoFilesFound".Translate() + storyDir;
                elements.Add(new ContentElement { isImage = false, text = rawStoryText });
                return;
            }

            // Sort by write time, newest first
            Array.Sort(files, (a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
            
            try
            {
                rawStoryText = File.ReadAllText(files[0]);
                ParseStoryContent(rawStoryText);
            }
            catch (Exception ex)
            {
                rawStoryText = "DAP_StoryBook_ErrorLoading".Translate() + ex.Message;
                elements.Add(new ContentElement { isImage = false, text = rawStoryText });
            }
        }

        private void ParseStoryContent(string content)
        {
            string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            
            // Regex to match markdown images: ![alt](url)
            Regex imgRegex = new Regex(@"^!\[(.*?)\]\((.*?)\)$");
            
            System.Text.StringBuilder currentText = new System.Text.StringBuilder();

            foreach (string line in lines)
            {
                Match match = imgRegex.Match(line.Trim());
                if (match.Success)
                {
                    // Flush accumulated text first
                    if (currentText.Length > 0)
                    {
                        elements.Add(new ContentElement { isImage = false, text = currentText.ToString() });
                        currentText.Clear();
                    }

                    string imgFilename = match.Groups[2].Value;
                    string absolutePath = Path.Combine(storyDir, imgFilename);

                    Texture2D tex = null;
                    if (File.Exists(absolutePath))
                    {
                        if (!loadedTextures.TryGetValue(absolutePath, out tex))
                        {
                            try
                            {
                                byte[] bytes = File.ReadAllBytes(absolutePath);
                                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                                if (ImageConversion.LoadImage(tex, bytes))
                                {
                                    loadedTextures[absolutePath] = tex;
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warning("[Story Engine] Failed to load comic panel texture: " + ex.Message);
                            }
                        }
                    }

                    elements.Add(new ContentElement 
                    { 
                        isImage = true, 
                        imagePath = absolutePath, 
                        texture = tex,
                        text = match.Groups[1].Value, // Alt text (e.g. Panel_1)
                        missingText = string.Format("DAP_StoryBook_MissingPanel".Translate(), Path.GetFileName(absolutePath))
                    });
                }
                else
                {
                    currentText.AppendLine(line);
                }
            }

            if (currentText.Length > 0)
            {
                elements.Add(new ContentElement { isImage = false, text = currentText.ToString() });
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 40f), titleText);
            Text.Font = GameFont.Small;

            Rect outRect = new Rect(inRect.x, inRect.y + 50f, inRect.width, inRect.height - 100f);
            
            // First pass: calculate total height of scroll area
            float totalHeight = 0f;
            float elementWidth = outRect.width - 24f; // subtract scrollbar width

            foreach (var el in elements)
            {
                if (el.isImage)
                {
                    if (el.texture != null)
                    {
                        // Scale image to fit width (keeping 1:1 aspect ratio or using native size)
                        float imgHeight = Mathf.Min(el.texture.height, el.texture.height * (elementWidth / el.texture.width));
                        totalHeight += imgHeight + 15f; // image + padding
                    }
                    else
                    {
                        totalHeight += 25f; // spacer/missing text height
                    }
                }
                else
                {
                    totalHeight += Text.CalcHeight(el.text, elementWidth) + 10f; // text + padding
                }
            }

            Rect viewRect = new Rect(0, 0, elementWidth, totalHeight + 40f);
            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);

            float currentY = 0f;
            foreach (var el in elements)
            {
                if (el.isImage)
                {
                    if (el.texture != null)
                    {
                        float imgWidth = Mathf.Min(el.texture.width, elementWidth);
                        float imgHeight = el.texture.height * (imgWidth / el.texture.width);
                        
                        // Center image if smaller than width
                        float startX = (elementWidth - imgWidth) * 0.5f;
                        Rect imgRect = new Rect(startX, currentY, imgWidth, imgHeight);
                        
                        GUI.DrawTexture(imgRect, el.texture, ScaleMode.ScaleToFit);
                        currentY += imgHeight + 15f;
                    }
                    else
                    {
                        Rect missingRect = new Rect(0f, currentY, elementWidth, 25f);
                        GUI.color = new Color(0.7f, 0.3f, 0.3f);
                        Widgets.Label(missingRect, el.missingText);
                        GUI.color = Color.white;
                        currentY += 25f;
                    }
                }
                else
                {
                    float textHeight = Text.CalcHeight(el.text, elementWidth);
                    Rect textRect = new Rect(0f, currentY, elementWidth, textHeight);
                    Widgets.Label(textRect, el.text);
                    currentY += textHeight + 10f;
                }
            }

            Widgets.EndScrollView();

            // Footer actions
            Rect footerRect = new Rect(inRect.x, inRect.yMax - 40f, inRect.width, 30f);
            
            if (Widgets.ButtonText(new Rect(footerRect.x, footerRect.y, 150f, 30f), openFolderText))
            {
                if (Directory.Exists(storyDir))
                {
                    Application.OpenURL("file://" + storyDir.Replace("\\", "/"));
                }
            }

            if (Widgets.ButtonText(new Rect(footerRect.x + 160f, footerRect.y, 150f, 30f), reloadStoryText))
            {
                LoadLatestStory();
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            // Free texture memory
            foreach (var tex in loadedTextures.Values)
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
            loadedTextures.Clear();
            elements.Clear();
        }
    }
}
