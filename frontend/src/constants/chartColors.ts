/**
 * Chart color constants for consistent visualization across the app.
 * Uses CSS variables where possible for theme consistency.
 */

// Pie chart color palette - 12 distinct colors for asset allocation
export const PIE_CHART_COLORS = [
  '#E8967B', // accent-peach
  '#F5E6C8', // accent-cream
  '#D4B896', // accent-sand
  '#F0D78C', // accent-butter
  '#8FBC8F', // DarkSeaGreen
  '#87CEEB', // SkyBlue
  '#DDA0DD', // Plum
  '#F0E68C', // Khaki
  '#98D8C8', // Aquamarine variant
  '#F7CAC9', // Rose Quartz
  '#B39DDB', // Light Purple
  '#90CAF9', // Light Blue
];

// Extended palette for more positions (cycles through with opacity variations)
export const getChartColor = (index: number): string => {
  return PIE_CHART_COLORS[index % PIE_CHART_COLORS.length];
};

// Bar chart colors for performance comparison
export const BAR_CHART_COLORS = {
  positive: '#22C55E', // green-500
  negative: '#EF4444', // red-500
  neutral: '#6B7280', // gray-500
  benchmark: '#60A5FA', // blue-400
};

// Gradient definitions for advanced charts
export const GRADIENT_COLORS = {
  profit: {
    start: '#22C55E',
    end: '#4ADE80',
  },
  loss: {
    start: '#EF4444',
    end: '#F87171',
  },
};
