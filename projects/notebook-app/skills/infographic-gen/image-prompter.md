# Image Prompt Generator for Infographic Generation

This module constructs the final image generation prompt by combining the infographic description with domain-specific rendering instructions.

## Your Task

You are implementing **Step 2** of the infographic generation workflow. You must:

1. Receive the infographic description, detected domain, and selected variant from Step 1
2. Load the appropriate rendering instructions based on domain and variant
3. Construct the complete image generation prompt
4. Save the prompt to a text file for transparency and debugging

## Input Parameters

From Step 1 (content-analyzer.md), you will receive:
- `description`: Detailed infographic description (2000-3000 characters)
- `domain`: Detected content domain (e.g., "business", "nature-space", "lifestyle", etc.)
- `variant`: Selected style variant (e.g., "editorial", "cosmic-wonder", "playful-learning")

## Rendering Instructions by Domain and Variant

Select the appropriate rendering instructions based on the domain and variant combination:

---

## Business Domain

### editorial Variant

```
### EDITORIAL MAGAZINE STYLE (Professional)

#### Color Palette:
- Background: Deep navy (#1A2332) or charcoal (#2D3436) with subtle texture
- Primary: Burgundy/wine red (#8B2635) for bold headlines and accents
- Accent: Gold metallic (#D4AF37) for premium feel, use sparingly
- Text: White (#FFFFFF) for titles, light gray (#E8E8E8) for body

#### Typography:
- Headlines: Extra bold geometric sans-serif (72pt+), dramatic size contrast
- Subheadings: Bold serif for elegant callouts, 36-48pt
- Body text: Clean sans-serif, 16-18pt, generous line spacing (1.6x)
- Pull quotes: Large italic serif, 48pt+, used as visual anchors

#### Layout & Composition:
- Asymmetric split layout with 60/40 or 70/30 proportions
- Overlapping photo blocks with text wraps creating depth
- Angled section dividers (diagonal lines at 15-20 degrees)
- Content blocks break grid intentionally for editorial feel
- Large photography crops as visual anchors

#### Visual Elements:
- High-contrast photography with dramatic cropping
- Bold geometric color blocks for section separation
- Minimal icons (solid or duotone style), used sparingly
- Data visualizations styled with brand colors, thick lines
- White space used dramatically, not just as filler

#### Mood & References:
- Confident, authoritative, premium magazine aesthetic
- Think: Bloomberg Businessweek covers, The Economist infographics
- Sophisticated but accessible, not stuffy
- Editorial photography style (not stock photos)

#### Key Features:
- Dramatic hierarchy (huge vs small, not gradual)
- Bold pull quotes as design elements
- Unexpected angles and overlaps
- Information density balanced with breathing room

#### Avoid:
- Cute rounded corners or playful elements
- Pastel or soft colors
- Symmetrical centered layouts
- Generic stock icons
- Overly decorative flourishes
```

### minimal-data Variant

```
### MINIMAL DATA STYLE (Professional)

#### Color Palette:
- Background: Pure white (#FFFFFF) or very light gray (#F8F9FA)
- Primary: Single bold accent - electric blue (#0066FF) OR deep forest green (#006B3F)
- Data colors: Monochromatic variations of primary (3-4 shades max)
- Text: Near-black (#1A1A1A) for perfect contrast

#### Typography:
- All text: Clean geometric sans-serif (Inter, SF Pro Display, Helvetica Neue)
- Headlines: Bold weight, 48-64pt, minimal letter spacing
- Body: Regular weight, 14-16pt, optimal line height (1.5x)
- Data labels: Medium weight, 12-14pt, uppercase for emphasis
- Consistent font sizing system (scale: 12, 14, 16, 24, 48, 64)

#### Layout & Composition:
- Grid-based but with generous margins (20-25% whitespace minimum)
- Centered content zones with symmetrical padding
- Clear visual hierarchy through size and spacing (not color)
- Elements aligned to precise grid (8pt or 12pt baseline)
- Maximum 3-4 content sections to maintain clarity

#### Visual Elements:
- Clean line charts with minimal gridlines (1-2 reference lines max)
- Simple bar graphs with rounded ends (4px radius)
- Minimalist icons (outline style, 2px stroke, no fills)
- Subtle drop shadows only where necessary (2px blur, 10% opacity)
- No decorative elements - every element serves a purpose

#### Mood & References:
- Clear, precise, maximum signal-to-noise ratio
- Think: Apple Keynote slides, Stripe documentation, Linear app
- Data speaks for itself without embellishment
- Swiss design principles - form follows function

#### Key Features:
- 40-50% whitespace as design element
- One focal point per section
- Consistent spacing system throughout
- Perfect alignment and symmetry
- Breathing room around every element

#### Avoid:
- Gradients, textures, or busy backgrounds
- Multiple accent colors or color coding
- Overlapping elements or layering
- Decorative graphics or illustrations
- Information overload
```

### corporate Variant

```
### CORPORATE REPORT STYLE (Professional)

#### Color Palette:
- Background: White (#FFFFFF) with light gray sections (#F5F7FA)
- Primary: Professional navy (#002B5C) for headers and key data
- Secondary: Medium gray (#5A6C7D) for supporting text
- Accent: Warm orange (#FF6B35) for highlights and CTAs
- Chart colors: 4-5 color palette (blue, teal, orange, gray, green)

#### Typography:
- Headers: Bold Arial/Helvetica, 32-48pt, traditional business feel
- Subheaders: Medium weight, 24-28pt, clear hierarchy
- Body: Regular Arial/Helvetica, 14-16pt, high readability
- Captions: Small text, 11-12pt, gray color for de-emphasis
- Numbers/data: Tabular figures for alignment, bold for emphasis

#### Layout & Composition:
- Structured sections with clear dividers (1-2px lines)
- Boxed information panels with subtle borders
- Consistent left/right margins (15-20% of width)
- Numbered sections and subsections for easy reference
- Header/footer with page info and branding

#### Visual Elements:
- Professional charts: pie charts, bar graphs, line graphs with legends
- Data tables with alternating row colors (zebra striping)
- Icon badges for categories (solid style, corporate palette)
- Process diagrams with numbered steps and arrows
- Callout boxes with border and light background fill

#### Mood & References:
- Trustworthy, organized, institutional authority
- Think: McKinsey reports, Deloitte insights, corporate annual reports
- Formal but not boring - professional polish
- Information-dense but well-organized

#### Key Features:
- High information density with clear organization
- Consistent visual system (colors, icons, charts)
- Clear sectioning with numbered hierarchy
- Source citations prominent (footnotes, references)
- Professional polish in every detail

#### Avoid:
- Artistic or unconventional layouts
- Trendy design elements or effects
- Playful colors or casual typography
- Asymmetry or broken grids
- Oversimplification - show the complexity
```

---

## Lifestyle Domain

### zen-minimal Variant

```
### ZEN MINIMAL STYLE (Lifestyle - Wellness & Mindfulness)

⚠️ CRITICAL LANGUAGE REQUIREMENT:
- ALL text must be in ENGLISH alphabet only (A-Z, a-z, 0-9)
- "Zen Minimal" refers to the AESTHETIC STYLE (simplicity, whitespace, wabi-sabi)
- NOT the language - all content must be readable English text

#### Color Palette:
- Background: Natural cream (#F5F5DC) or soft beige (#E8DCC4)
- Primary: Soft gray (#B8B8B8) for text and lines
- Accent: Single earth tone - clay (#C4A57B) OR charcoal (#4A4A4A)
- Highlight: Very subtle - warm sand (#D4C5B0) or cool stone (#C5C9C7)
- Maximum 3-4 colors total including background

#### Typography:
- All text: Light to regular weight, never heavy bold - ENGLISH TEXT ONLY
- Headlines: Elegant sans-serif or serif, 36-48pt, generous spacing
- Body: Light sans-serif, 14-16pt, 2.0x line height for breathing room
- Haiku-like brevity in ENGLISH - fewer words, more meaning
- Perfect kerning and spacing - precision matters

#### Layout & Composition:
- Zen-like composition with intentional emptiness (60-70% whitespace)
- Asymmetric balance inspired by ikebana (flower arrangement)
- Golden ratio proportions (1:1.618) for section divisions
- Minimal elements with maximum impact
- Negative space as primary design element

#### Visual Elements:
- Simple line drawings with minimal strokes - ONLY when essential
- Negative space illustrations (what's NOT drawn is as important)
- Natural textures (paper grain, wood texture) - VERY subtle
- Small accent marks (single dot or shape for emphasis) - RARE use
- NO borders, NO dividing lines, NO decorative rules

#### Mood & References:
- Calm, meditative, contemplative quiet
- Think: Muji aesthetic, Japanese tea ceremony, wabi-sabi philosophy
- Imperfect perfection - embrace natural imperfection
- Empty space is design

#### Key Features:
- Extreme restraint - resist adding more
- Breathing room around every element (minimum 40px margins)
- Fewer elements with deeper presence
- Quiet elegance - whispers, not shouts
- Separation through whitespace, NOT lines

#### Avoid:
- ❌ ANY Japanese, Chinese, Korean, or non-English text characters
- ❌ ANY horizontal or vertical dividing lines between sections
- ❌ Borders around content areas or images
- ❌ Underlines or strikethroughs for emphasis
- Bold vibrant colors or high saturation
- Information density or cramping
```

### vibrant-lifestyle Variant

```
### VIBRANT LIFESTYLE STYLE (Fitness & Energy)

#### Color Palette:
- Background: Bright white (#FFFFFF) or vibrant color blocks
- Primary: Bold saturated colors - hot pink (#FF006E), electric blue (#00C2FF)
- Secondary: Sunshine yellow (#FFD60A), lime green (#B7FF00), coral (#FF7F51)
- Use 4-5 bold colors simultaneously - embrace color chaos
- High contrast combinations for maximum pop

#### Typography:
- Headlines: Fun rounded sans-serif (Quicksand, Fredoka), 48-72pt, BOLD
- Body: Geometric sans-serif, 16-20pt (large for readability)
- Varied sizes for visual rhythm (mix 12pt, 24pt, 48pt, 72pt+)
- ALL CAPS for emphasis, lowercase for casual feel
- Playful text on curved paths or circles

#### Layout & Composition:
- Dynamic diagonal angles (15-30 degrees) for energy
- Sticker-like elements scattered asymmetrically
- Layered composition with 4-5 depth levels
- Intentional "organized chaos" - busy but navigable
- Mobile-first thinking - bold clear elements

#### Visual Elements:
- Colorful abstract shapes (blobs, circles, stars, lightning bolts)
- Gradient meshes (multi-color gradients, 3-4 colors)
- Sticker overlays (3D-looking stickers with shadows)
- Playful icons (rounded, friendly, expressive faces)
- Speech bubbles and callout shapes
- Pattern fills (dots, stripes, doodles)

#### Mood & References:
- Energetic, youthful, optimistic, playful
- Think: TikTok/Instagram Stories, Gen-Z aesthetic
- Social media native design language

#### Key Features:
- High energy through color and movement
- Color blocking with multiple bold colors
- Playful overlays and stickers
- Motion implied through angles and overlaps
- Separation through color blocks and shapes, NOT lines

#### Avoid:
- ❌ Thin horizontal lines separating sections
- ❌ Border strokes around elements
- ❌ Frame outlines
- Conservative corporate colors
- Formal structured layouts
```

### editorial-lifestyle Variant

```
### ILLUSTRATED EDITORIAL STYLE (Lifestyle/Educational)

#### Color Palette:
- Background: Warm cream (#F5F2E8) or soft beige (#E8DCC4)
- Primary earth tones: Earth brown (#8B6F47), Sky blue (#7BA7BC), Forest green (#5A7553)
- Accent colors: Sunset orange (#D4765F), Dusk purple (#8B7BA8), Dawn yellow (#F4C542)
- Text: Deep brown-black (#2C2416), Medium brown (#5A4A3A), Light brown (#8B7355)

#### Typography:
- Headlines: Friendly serif or rounded sans-serif, 36-56pt, Bold
- Body text: Humanist sans-serif, 15-18pt, line height 1.7x
- Labels: Semi-bold, 12-14pt

#### Layout & Composition:
- Circular card design with illustration circles (120-200px diameter)
- Text cards with rounded corners (12-20px radius)
- Asymmetric balance, S-curve or zigzag reading path
- Minimum 24px between elements

#### Visual Elements:
- Hand-drawn digital illustrations in circular frames
- Warm, organic, slightly imperfect aesthetic
- Medium detail level (not simplified, not photorealistic)
- Decorative elements: Hand-drawn icons, dotted lines, wavy curves
- Subtle texture overlays (paper, watercolor, canvas)

#### Mood & References:
- Friendly without being childish
- Think: Airbnb Experiences, Duolingo, Headspace, National Geographic Kids
- Weekend family outing vibe

#### Key Features:
- Illustration-first approach
- Circle dominant (80% of elements use circles/rounded corners)
- Warm color palette
- Organic feel - embrace slight imperfections
- High readability

#### Avoid:
- ❌ Photography collages (must use illustrations)
- ❌ Cold colors or high-tech aesthetics
- ❌ Dense information blocks
- ❌ Pure black text
```

---

## Nature-Space Domain

### cosmic-wonder Variant

```
### SPACE/COSMIC STYLE (Astronomy & Space)

#### Color Palette:
- Background: Deep space gradient (#0A1628 → #1A0B2E → #16213E)
- Nebula overlay: Radial gradient with purple/blue hues (30-40% opacity)
- Primary: Starlight gold (#FFD700), Cosmic blue (#4A90E2), Nebula purple (#9D4EDD)
- Accent: Aurora green (#00FF88), Galaxy pink (#FF006E)
- Text: Bright white (#FFFFFF), Dim white (85% opacity), Caption gray (60% opacity)

#### Typography:
- Headlines: Futuristic geometric sans-serif (Orbitron, Exo, Space Grotesk), 48-72pt
- Letter spacing: 2-4% (wide tracking)
- Subtle glow effect: text-shadow 0 2px 20px rgba(255,215,0,0.5)
- Body: Regular geometric sans-serif, 14-18pt, 85% white

#### Layout & Composition:
- Layered depth (5 distinct Z-layers):
  1. Background: Deep space gradient + star particles
  2. Nebula layer: Blurred colorful clouds
  3. Decorative: Constellation lines, orbital paths
  4. Content: Illustration cards, text blocks
  5. Foreground: Glow effects, lens flares

#### Visual Elements:
- Star particles (1-4px, 50-100 per 1000 square pixels)
- Nebula clouds with blur (40-80px Gaussian)
- Orbital paths (dashed curves)
- Light effects: lens flare, glow, light rays
- Digital painted illustrations (semi-realistic)
- Circular/orbital motifs

#### Mood & References:
- Mysterious, awe-inspiring, exploratory
- Think: NASA posters, Interstellar, Sky & Telescope magazine
- Grand cosmic scale

#### Key Features:
- Dark dominance (90% dark background, 10% bright accents)
- Glow everywhere (text, borders, icons, lines)
- Illustration-driven
- Rich layering (minimum 5 depth layers)
- Circular/orbital motifs

#### Avoid:
- ❌ Pure black background (use deep blue-purple gradients)
- ❌ Over-saturated neon colors
- ❌ Cartoon-style illustrations
- ❌ Flat design without depth
- ❌ Cluttered space
```

### nature-minimal Variant

```
### NATURE MINIMAL STYLE (Ecology & Environment)

#### Color Palette:
- Background: Soft cream (#FAF8F5) or pale green (#F0F4F0)
- Primary: Forest green (#4A7C59), Earth brown (#8B7355)
- Accent: Sage (#9CAF88), Clay (#C4A57B)
- Text: Dark charcoal (#2C3E50)

#### Typography:
- Clean organic sans-serif
- Headlines: 36-48pt, regular to medium weight
- Body: 14-16pt, generous line spacing (1.8x)

#### Layout & Composition:
- Clean, peaceful composition
- 50-60% whitespace
- Organic shapes and curves
- Balanced asymmetry

#### Visual Elements:
- Simple botanical illustrations
- Organic shapes (leaves, waves, stones)
- Minimal line art
- Natural photography (when needed)
- Subtle textures (paper, linen)

#### Mood & References:
- Calm, serene, sustainable
- Think: Patagonia, REI educational materials
- Environmental awareness

#### Key Features:
- Clean and peaceful
- Nature-inspired shapes
- Sustainable aesthetic
- Breathing room

#### Avoid:
- Busy patterns
- Harsh contrasts
- Industrial elements
```

### outdoor-adventure Variant

```
### OUTDOOR ADVENTURE STYLE (Hiking & Exploration)

#### Color Palette:
- Background: Sky blue to cream gradient OR warm earth tones
- Primary: Adventure orange (#FF6B35), Mountain blue (#2C5F8D)
- Accent: Trail green (#7BA05B), Sunset red (#E07A5F)
- Text: Deep charcoal (#2C3E50)

#### Typography:
- Bold, rugged sans-serif
- Headlines: 42-56pt, bold weight
- Body: 16-18pt, high readability

#### Layout & Composition:
- Dynamic layouts suggesting movement
- Hero images of landscapes
- Layered depth (foreground/midground/background)
- Trail-like flow between sections

#### Visual Elements:
- Landscape photography or illustrations
- Topographic map elements
- Trail markers and icons
- Compass roses, mountain silhouettes
- Adventure gear illustrations

#### Mood & References:
- Adventurous, energetic, inspiring
- Think: National Park posters, REI catalogs
- Outdoor exploration spirit

#### Key Features:
- Dynamic and energetic
- Nature-focused imagery
- Clear wayfinding elements
- Inspirational tone

#### Avoid:
- Indoor or urban elements
- Delicate or fragile aesthetics
```

---

## Technology Domain

### cyberpunk Variant

```
### CYBERPUNK DARK STYLE (Tech)

#### Color Palette:
- Background: Near-black (#0D1117) or very dark navy (#0A0E27)
- Primary neon: Cyan (#00FFFF), magenta (#FF00FF), electric green (#39FF14)
- Secondary: Deep purple (#6B2C91), electric blue (#0080FF)
- Text: White (#FFFFFF) with subtle cyan tint

#### Typography:
- Futuristic geometric sans-serif, 48-72pt, wide letter spacing
- Monospace (JetBrains Mono, Fira Code) for tech labels
- Glowing text effects on key words

#### Layout & Composition:
- Layered depth with glowing panels
- Holographic UI elements (20-30% opacity)
- Perspective grids in background
- Asymmetric tech panels with angled edges

#### Visual Elements:
- 3D isometric tech icons with glowing edges
- Circuit board patterns
- Hexagonal grids
- Glowing nodes and connection lines
- Data stream visualizations
- Neon outline icons (2-3px stroke with glow)

#### Mood & References:
- Futuristic, high-tech, cyberpunk aesthetic
- Think: Blade Runner, Tron, Cyberpunk 2077

#### Key Features:
- Neon glow effects everywhere
- Dark mode supreme
- Tech-heavy visuals
- Layered depth with transparency

#### Avoid:
- Warm colors
- Organic shapes
- Light backgrounds
```

### notion-isometric Variant

```
### NOTION FRIENDLY ILLUSTRATION STYLE (Productivity)

#### Color Palette:
- Background: Light gradient (#FDFEFF → #F0F9FF) or white
- Organic shapes: Soft pastels with gradients (coral, mint, butter yellow, sky blue)
- UI elements: White cards (#FFFFFF)
- Text: Dark charcoal (#2C3E50), medium gray (#5A6C7D)
- Shadows: Soft single shadow (0-4px offset, 20px blur, 30% opacity)

#### Typography:
- Friendly sans-serif (Poppins, Quicksand, Nunito, Circular)
- Headlines: Bold or SemiBold, 36-48pt
- Body: Regular, 16-18pt
- Generous line height (1.6-1.8)

#### Layout & Composition:
- Simplified 2.5D depth (gentle layering, 25-35° angles)
- Light and airy spacing
- Floating elements with soft shadows
- Organic flow with curved paths
- Optional color zones for narrative

#### Visual Elements:
- Cards with minimal depth, rounded corners (12-20px)
- Organic blob shapes (decorative, light gradients)
- Symbolic illustrations (theme-based, friendly)
- UI mockup elements (simplified)
- Curved connection lines

#### Mood & References:
- Friendly, approachable, optimistic
- Think: Notion illustrations, Slack graphics, Dropbox Paper

#### Key Features:
- Light backgrounds
- Soft pastel colors
- Simplified 2.5D
- Friendly typography
- Generous white space

#### Avoid:
- ❌ Dark backgrounds
- ❌ Strict geometric 3D
- ❌ Harsh shadows
- ❌ Corporate stiffness
```

### data-dashboard Variant

```
### DATA DASHBOARD STYLE (Analytics & Metrics)

#### Color Palette:
- Background: Dark navy (#1A202C) or charcoal (#2D3748)
- Primary: Electric blue (#3B82F6), Teal (#14B8A6)
- Data colors: Multi-color palette for data viz
- Text: White (#FFFFFF) and light gray (#E2E8F0)

#### Typography:
- Monospace for numbers (JetBrains Mono)
- Clean sans-serif for labels
- Bold numbers for emphasis

#### Layout & Composition:
- Grid-based dashboard layout
- Card-based modules
- Clear data hierarchy
- Metric-focused design

#### Visual Elements:
- Clean charts and graphs
- KPI cards with big numbers
- Trend indicators (arrows, sparklines)
- Progress bars and gauges
- Table data with highlighting

#### Mood & References:
- Professional, analytical, data-driven
- Think: Modern dashboards, analytics tools

#### Key Features:
- Dark mode optimized
- Data visualization focused
- Clean and professional
- Metric prominence

#### Avoid:
- Decorative elements
- Light backgrounds (dark mode preferred)
```

---

## Family Domain

### playful-learning Variant

```
### PLAYFUL LEARNING STYLE (Kids Activities)

#### Color Palette:
- Background: Soft gradient (sky blue to cream, warm yellow to peach, soft green to mint)
- Primary: Bright saturated color (#FF5252 red, #2196F3 blue, #4CAF50 green)
- Secondary: Complementary brights (orange, purple, teal)
- Accent: Soft pastels for backgrounds
- Text: Deep charcoal (#2C3E50)

#### Typography:
- Headers: Bold rounded sans-serif (Baloo, Nunito, Quicksand), 48-60pt
- Body: Friendly sans-serif, 18-22pt (optimized for young readers)
- Step numbers: Extra-bold 72-96pt in circular badges

#### Layout & Composition:
- Best with DUAL-PATH or FLOW layouts
- Hero illustration (250-300px circle)
- Chunky content cards (min 200px width, 24px corner radius)
- Flowing connectors between steps
- Footer band for adult/teacher guidance

#### Visual Elements:
- Friendly illustrated characters
- Context-appropriate icons
- Sticker-style badges
- Motion lines showing action
- Chunky numbered badges (80-100px)
- Speech bubbles

#### Mood & References:
- PBS Kids, Sesame Street Workshop
- Encouraging, safe, inclusive

#### Key Features:
- Large hero illustration
- Every step has: number + icon + short instruction + illustration
- Safety callouts in speech bubbles
- Visual flow with contextual connectors
- Subtle themed texture or pattern

#### Avoid:
- Small fonts or thin strokes
- Overly saturated backgrounds
- Scary or mature themes
- Corporate or academic formality
- Any non-English text
```

### educational-story Variant

```
### EDUCATIONAL STORY STYLE (Kids Learning)

#### Color Palette:
- Background: Warm textured paper (#FFF9EE, #F5F5DC, #FDF6E3)
- Primary: Rich story colors (deep blue #3D7DFF, forest green #6CCF64, golden yellow #FFD447)
- Accent: Jewel tones (amethyst, ruby, emerald)
- Shadows: Soft colored shadows for layered paper effect

#### Typography:
- Titles: Friendly bold fonts (Fredoka, Chewy, Baloo), 52-68pt
- Section headers: Rounded sans-serif, 24-32pt
- Body: Large clear sans-serif, 18-22pt
- Narrative callouts: Slightly handwritten feel

#### Layout & Composition:
- Works with FLOW, DUAL-PATH, or RADIAL
- Hero styled as open storybook
- Content panels as "pages" with book-like treatments
- Story progression markers
- Decorative connectors (ribbons, pennant flags, illustrated paths)

#### Visual Elements:
- Illustrated characters and scenes
- Layered collage aesthetic (cut-paper effect)
- Icon badges as tangible objects (crayons, stamps, stickers)
- Educational overlays (graph paper, lined paper, maps)
- Frame variations (polaroid, washi tape, paper clips)

#### Mood & References:
- Children's museum exhibits, Highlights magazine
- PBS educational programming, Scholastic materials

#### Key Features:
- Narrative structure (beginning, middle, end)
- Large friendly numbers in decorative badges
- "Did you know?" callouts in decorative boxes
- Visual texture throughout
- Thematic decorative elements

#### Avoid:
- Serious academic tone
- Thin or script-heavy fonts
- Dense text blocks
- Dark or moody palettes
- Realistic violence
- Non-English decorative text
```

### illustrated-adventure Variant

```
### ILLUSTRATED ADVENTURE STYLE (Family-Friendly Exploration)

Use the Illustrated Editorial Style (lifestyle/editorial-lifestyle variant) with family-friendly themes:
- Exploration and discovery focus
- Adventure without being extreme
- Safe for all ages
- Quest-like narrative structure
```

### friendly-guide Variant

```
### FRIENDLY GUIDE STYLE (How-To for Families)

Use the Playful Learning Style with simplified language:
- Step-by-step guidance
- Helpful and supportive tone
- Clear beginner instructions
- Patient and encouraging
```

---

## Science Domain

### academic-paper Variant

```
### ACADEMIC PAPER STYLE (Scientific Research)

#### Color Palette:
- Background: Pure white (#FFFFFF)
- Text: True black (#000000)
- Accent: Deep blue (#1E3A8A) OR forest green (#065F46)
- Charts: Grayscale with single accent

#### Typography:
- Body: Clean serif (Georgia, Merriweather), 14-16pt
- Headlines: Bold sans-serif, 24-32pt
- Technical terms: Monospace, 14pt
- Line spacing: 1.6-1.8x

#### Layout & Composition:
- Hierarchical structure (1, 1.1, 1.2 numbering)
- Generous margins
- Numbered sections
- Figure captions below images
- Bibliography at bottom

#### Visual Elements:
- Simple diagrams with clear labels
- Flowcharts with standard shapes
- Clean line illustrations
- Properly formatted equations
- Citation numbers in brackets

#### Mood & References:
- Scholarly, precise, authoritative
- Think: Academic papers, IEEE publications

#### Key Features:
- Clarity and readability first
- Consistent formatting
- Proper citations
- Content-first approach

#### Avoid:
- Flashy effects
- Decorative elements
- Vibrant colors
```

### lab-visual Variant

```
### LAB VISUAL STYLE (Experimental Science)

#### Color Palette:
- Background: Clinical white (#FFFFFF) or light gray
- Primary: Lab blue (#0066CC), Safety orange (#FF6600)
- Accent: Medical green (#00A86B)

#### Typography:
- Clean sans-serif
- Technical labels prominent

#### Layout & Composition:
- Clean, organized sections
- Laboratory equipment imagery
- Process diagrams
- Step-by-step procedures

#### Visual Elements:
- Lab equipment illustrations
- Chemical formulas
- Safety symbols
- Measurement indicators

#### Mood & References:
- Clinical, precise, scientific method

#### Key Features:
- Safety-conscious
- Procedure-focused
- Technical accuracy
```

### illustrated-science Variant

```
### ILLUSTRATED SCIENCE STYLE (Educational)

#### Color Palette:
- Background: Soft white or light blue
- Primary: Science blue (#2196F3), Nature green (#4CAF50)
- Accent: Discovery purple (#9C27B0)

#### Typography:
- Friendly but scientific
- Clear explanations

#### Layout & Composition:
- Concept-focused
- Visual metaphors
- Clear process flows

#### Visual Elements:
- Illustrated scientific concepts
- Simplified diagrams
- Labeled illustrations
- Process visualizations

#### Mood & References:
- Educational, approachable
- Think: National Geographic, educational textbooks

#### Key Features:
- Concept visualization
- Clear explanations
- Engaging illustrations
```

---

## History Domain

### timeline-classic Variant

```
### TIMELINE CLASSIC STYLE (Historical Events)

Classic historical timeline with vintage textures and period-appropriate typography.
Use sepia tones and archival aesthetics.

#### Key Features:
- Vintage paper textures
- Sepia or aged color palette
- Period-appropriate typography
- Timeline-based layout
- Historical imagery
```

### documentary-style Variant

```
### DOCUMENTARY STYLE (Historical Documentation)

Documentary film aesthetic with cinematic framing.
Bold historical imagery with modern clean typography for contrast.

#### Key Features:
- Cinematic composition
- Bold historical imagery
- Modern typography contrast
- Dramatic lighting
- Archival photo integration
```

### cultural-heritage Variant

```
### CULTURAL HERITAGE STYLE (Cultural Preservation)

Cultural preservation aesthetic with museum-quality presentation.
Rich earth tones and traditional design elements.

#### Key Features:
- Museum-quality presentation
- Rich earth tones
- Traditional design elements
- Respectful treatment
- Cultural artifacts integration
```

---

## Social Domain

### pinterest-collage Variant

```
### PINTEREST COLLAGE STYLE (Social Inspiration)

#### Color Palette:
- Background: Soft cream or warm white
- Pastels: Blush pink, sage green, terracotta
- Accent: Dusty rose or muted gold

#### Visual Elements:
- Collage-style photo blocks
- Varied angles (0-5 degree tilts)
- Overlapping layers
- Hand-drawn doodles (sparingly)
- Botanical elements

#### Mood & References:
- Warm, inviting, Pinterest mood boards

#### Key Features:
- Photo-heavy
- Mixed media layering
- Organic arrangement
- Decorative flourishes (sparingly)
```

### instagram-modern Variant

```
### INSTAGRAM MODERN STYLE (Social Media)

#### Color Palette:
- Clean whites and bright accents
- Instagram-ready color palette
- High contrast

#### Visual Elements:
- Story-format inspiration
- Mobile-optimized
- Clean modern aesthetic
- Social media native elements

#### Mood & References:
- Instagram Stories aesthetic
- Mobile-first design
```

### influencer-bold Variant

```
### INFLUENCER BOLD STYLE (Personal Branding)

#### Color Palette:
- Bold, attention-grabbing colors
- High saturation
- Confident contrasts

#### Visual Elements:
- Bold typography
- Confident imagery
- Strong personal brand elements
- Attention-grabbing design

#### Mood & References:
- Bold, confident, viral-ready
- Personal brand aesthetic
```

---

## Prompt Construction

After selecting the appropriate rendering instructions, construct the final image prompt:

### Template:

```
Create a professional, educational infographic in LANDSCAPE format (16:9 aspect ratio).

{Insert the infographic description from Step 1}

{Insert the selected rendering instructions based on domain/variant}

## GENERAL RENDERING RULES:
- LANDSCAPE orientation (16:9 or 4:3 aspect ratio)
- ALL TEXT MUST BE IN ENGLISH ONLY - no Chinese, Japanese, Korean, or other languages
- Balance aesthetics with readability
- Create visual hierarchy through size, color, and positioning
- Include whitespace for breathing room
- Make content scannable (bullets ≤12 words)
- Professional educational aesthetic
- DO NOT include decorative non-English scripts even if style suggests it

If there are sensitive figures or copyrighted content, draw a similar alternative.
IMPORTANT: Render every label in clear English only with correct spelling and spacing.
```

## Output

1. Construct the complete prompt using the template above
2. Save the prompt to a text file: `{output_dir}/{input_filename}_{timestamp}_prompt.txt`
3. Return the final prompt string to be used in Step 3 (image generation)

## Implementation Notes

- Use Read tool to load this file and find the appropriate rendering instructions
- Match the domain and variant exactly to select the correct style block
- If variant not found, use the first variant in that domain as default
- If domain not found, use Business/editorial as fallback
- Ensure the final prompt is 3000-5000 characters (description + instructions + general rules)
