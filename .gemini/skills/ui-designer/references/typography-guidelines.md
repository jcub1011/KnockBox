# Typography & Color Guidelines

This guide focuses on making bold, distinctive choices for typography and color palettes, avoiding generic "AI" aesthetics.

## Typography Strategies

Avoid the "Big Three" (Inter, Roboto, Arial) and common "AI" defaults.

### Pairing Distinctive Fonts
Always pair a high-character **Display Font** with a highly readable **Body Font**.

- **Display (Serif) + Body (Sans)**: Classic, refined, editorial.
  - Example: *Cormorant Garamond* (Display) + *Outfit* (Body).
- **Display (Sans) + Body (Serif)**: Modern, professional, authoritative.
  - Example: *Space Grotesk* (Display) + *Crimson Text* (Body).
- **Display (Mono) + Body (Sans)**: Brutalist, industrial, technical.
  - Example: *JetBrains Mono* (Display) + *Plus Jakarta Sans* (Body).
- **Display (Script/Handwritten) + Body (Sans)**: Organic, playful, custom.
  - Example: *Satisfy* (Display) + *Montserrat* (Body).

### Creative Styling
- **Letter Spacing**: Use `letter-spacing: -0.05em` for tight display headers or `0.1em` for wide, airy subheaders.
- **Line Height**: Use tight line-heights (1.1 - 1.2) for large headers and generous line-heights (1.6 - 1.8) for body text.
- **Vertical Text**: Use `writing-mode: vertical-rl` for unexpected layout accents.

## Color Palette Strategies

Avoid timid, evenly-distributed palettes. Commit to a dominant mood.

### High Contrast (The "B&W+" Strategy)
Use pure black or pure white as the dominant base, with ONE high-impact accent color (e.g., Neon Green, Electric Blue, Hot Pink).

### Monochromatic with Texture
Use variations of a single hue, but add depth with gradients, noise, and opacity.

### Unexpected Duotones
Pair two contrasting colors (e.g., Deep Purple + Bright Orange, Forest Green + Soft Pink) and use them across all UI elements.

### CSS Variables for Themes
Always define a palette using CSS variables for consistency and ease of theme-switching.
```css
:root {
  --primary: #ff0055;
  --secondary: #1a1a1a;
  --accent: #00ffcc;
  --bg: #ffffff;
  --text: #1a1a1a;
  --surface: #f0f0f0;
}

[data-theme='dark'] {
  --bg: #0a0a0a;
  --text: #f0f0f0;
  --surface: #1a1a1a;
}
```

## Anti-Patterns (Avoid These)
- **Purple Gradients on White**: The generic "SaaS/AI" look.
- **Low Contrast Gray-on-Gray**: Hard to read and lacks character.
- **Default System Fonts**: Arial, Helvetica, Segoe UI, San Francisco (unless part of a specific "system" aesthetic).
