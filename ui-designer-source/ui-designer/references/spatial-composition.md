# Spatial Composition Guidelines

This guide focuses on non-traditional layout patterns to create unforgettable user interfaces.

## Unexpected Layouts

### Asymmetry
Break the "centered everything" habit. Use offsetting, varying column widths, and deliberate "voids" (empty space) to create visual tension and interest.

### Grid-Breaking
Overlap elements across grid lines. Use `z-index` to stack content in unexpected ways.
```css
.hero-text {
  grid-column: 1 / 8;
  z-index: 2;
}
.hero-image {
  grid-column: 6 / 12;
  margin-top: -100px;
  z-index: 1;
}
```

### Diagonal Flow
Use `transform: skewY()` or `clip-path` to create diagonal sections that break the horizontal rhythm.
```css
.skewed-section {
  transform: skewY(-5deg);
  margin-top: -50px;
  padding: 100px 0;
}
```

## Advanced Composition Patterns

### Layered Transparency
Use `backdrop-filter: blur()` and varying levels of `opacity` to create depth and a "glassmorphism" effect that feels intentional, not trendy.

### Horizontal Scrolling Sections
Use `overflow-x: scroll` for specific sections (like portfolios or cards) to break the vertical scroll expectation.

### Fixed Overlays
Use `position: fixed` or `sticky` for decorative elements (e.g., floating icons, navigation accents) that stay in place as the user scrolls, creating a "window" effect.

### Diagonal Text / Elements
Rotate text or buttons by small degrees (e.g., `-3deg`) to add a "hand-crafted" or "dynamic" feel.

## Spatial Anti-Patterns
- **Centered Cards in a Grid**: The "standard" portfolio layout. Break it with varying card sizes or staggered offsets.
- **Top-Down Vertical Everything**: Use horizontal flow, diagonal lines, and overlapping elements.
- **Uniform Spacing**: Use "staggered" spacing (e.g., `margin-bottom: 2rem` then `4rem` then `3rem`) to create rhythm.
