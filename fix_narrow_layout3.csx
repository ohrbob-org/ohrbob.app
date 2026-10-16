// The issue is likely that the right column Grid isn't actually disappearing entirely because the IsVisible binding might not affect the column layout correctly, or the second column is not stretching. Let's rethink the layout.
// Instead of a 3-column Grid, a DockPanel is perfect for this:
// Left: Dock Left
// Right: Dock Right (hides when narrow)
// Center: Fills remaining space, but we want it right-aligned when narrow.
// Actually, Grid is fine if we configure it right. Let's look at the AudioWidget.axaml again.
