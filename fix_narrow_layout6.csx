// The issue with my DockPanel vs Grid attempt: The inner Grid with Column="1" has StackPanel centered.
// I changed it to "Right" horizontally but it didn't align to the right edge. Wait, in the screenshot I saw it centered but the *slider* grid completely hid. The padding makes it look somewhat centered but slightly offset.
// No, the screenshot you provided shows the mobile layout of the web widget.
// The web widget mobile layout HIDES the station name, HIDES the back/forward buttons, HIDES the progress slider, HIDES the volume/settings.
// And it puts the Cover/Title on the left, and the remaining 3 controls (skip back, play, skip fwd) on the RIGHT!
// Ah, let's look at the SCSS:
// .audio-meta { flex: 1 1 auto; }
// This pushes the `.audio-controls` to the far right!
// So my layout was wrong because I had Grid Column 1 as "Auto" and Grid Column 0 as "*".
// If Grid Column 0 is "*" and Grid Column 1 is "Auto", the controls WILL be pushed right!
