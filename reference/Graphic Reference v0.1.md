# Graphic Reference v0.1

**Status:** Informational Reference Only

**Owner:** SvitloSk Publisher Project

**Applies to:** Graphic Publisher

---

# Purpose

This document defines the role of the reference graphic used during the implementation of the SvitloSk Publisher.

The accompanying image is **NOT** a specification.

It is **NOT** a UI design.

It is **NOT** a rendering template.

It is **NOT** a layout contract.

It exists solely to communicate the **expected class of graphical publication** produced by the Graphic Publisher.

---

# Scope

The reference graphic demonstrates:

- overall publication purpose;
- approximate information density;
- semantic grouping of information;
- expected visual complexity;
- expected publication format suitable for Telegram.

Nothing else should be inferred from the image.

---

# Architectural Rule

The Graphic Publisher SHALL be completely independent of this specific visual example.

Publisher business logic MUST NOT depend on:

- colors;
- typography;
- logo placement;
- spacing;
- iconography;
- borders;
- footer;
- QR location;
- branding elements;
- decorative graphics.

Changing any of these SHALL NOT require changes to Publisher business logic.

---

# Semantic Structure

The Publisher SHALL only guarantee generation of graphical publications containing semantic information equivalent to:

- publication title;
- publication date;
- outage schedule;
- time scale;
- queue identifiers;
- legend;
- publication metadata;
- service information.

The visual representation of these semantic elements may change between template versions.

---

# Template Independence

Graphic Publisher SHALL support multiple rendering templates.

The rendering template is considered an interchangeable implementation detail.

Changing or replacing a template SHALL NOT require modification of:

- Editorial Decision Engine;
- Edition Assembly;
- Delivery Pipeline;
- Synchronization Engine;
- Publication logic.

Only the Graphic Publisher implementation may be affected.

---

# Expected Output

Graphic Publisher produces an immutable **Graphic Artifact**.

Graphic Artifact represents a rendered publication and may contain:

- PNG;
- SVG;
- JPEG;
- future supported graphical formats.

The specific rendering technology is outside the scope of this reference.

---

# Versioning

This reference graphic is versioned independently from the Publisher architecture.

Current version:

```
Graphic Reference v0.1
```

Future versions may change visual appearance without changing Publisher architecture.

---

# Allowed Future Changes

The following may change at any time:

- SvitloSk branding;
- logo;
- typography;
- color palette;
- spacing;
- composition;
- legends;
- QR code placement;
- footer;
- warning blocks;
- information cards;
- graphic style;
- icons;
- decorative elements.

These changes SHALL NOT be considered architectural changes.

---

# Prohibited Assumptions

Developers MUST NOT assume that:

- the image defines exact coordinates;
- the image defines exact dimensions;
- the image defines final branding;
- the image defines final typography;
- the image defines final colors;
- the image defines the only supported template.

The image is a communication aid only.

---

# Relationship to Specifications

Normative behavior is defined exclusively by:

- Publisher Specification Repository
- Publisher Repository
- L3/L4 implementation specifications

If any discrepancy exists between this reference image and the specifications, the specifications always take precedence.

---

# Summary

The reference graphic communicates **what class of publication should exist**, not **how it must be drawn**.

Architecture remains template-independent.

Business logic remains presentation-independent.

Graphic Publisher remains replaceable.