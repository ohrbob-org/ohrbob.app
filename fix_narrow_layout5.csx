// The issue with DockPanel is that if Right is docked first, it takes only the space it needs (Auto). If we dock Left first, it takes Auto. And the last child (the slider/progress) can fill the remaining space!
// But if the slider is the last child, we want the controls to stay in the middle.
// Actually, Grid is the best for this IF we use `*` sizing correctly.
// Let's go back to Grid, but instead of hiding the right column, we put both the Center Controls and the Right Progress into the SAME column, or we just let Grid auto-size.
// If Grid Columns are: [Auto/Fixed for Left] [Auto for Controls] [* for Progress]
