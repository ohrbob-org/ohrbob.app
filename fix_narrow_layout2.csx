// Right alignment didn't work because the left column was bounded by `MaxWidth="350"`. 
// If the left column stops growing at 350px, it leaves empty space on the right of the center column, making it appear off-center rather than pinned to the right edge.
// We need to change the grid column structure to make the left column greedy or change the alignment.
