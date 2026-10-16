// Since Grid.Column="1" is centered, we need it to dynamically align right, OR we can just change the column structure so that when the right column vanishes, the center column naturally pushes right.
// Wait, Avalonia Grids don't "collapse" columns unless the ColumnDefinition Width is Auto or the element disappears.
// Currently Column 1 is Auto, Column 0 is * and Column 2 is 1.5*.
// If Column 2 hides, Column 0 (*) will take ALL the remaining space, pushing Column 1 (Auto) to the far right natively!
