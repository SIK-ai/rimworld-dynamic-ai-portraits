import re

with open('Source/UI_AIPortraitCard.cs', 'r') as f:
    content = f.read()

# Remove private const string LockedSuffix = "_locked";
content = re.sub(r'\s*private const string LockedSuffix = "_locked";\n', '\n', content)

with open('Source/UI_AIPortraitCard.cs', 'w') as f:
    f.write(content)
