# Demo visual fidelity ledger

Concept: `docs/demo-design-concept.png`

Desktop render: `docs/demo-implementation-desktop.jpg`

Mobile render: `docs/demo-implementation-mobile.png`

| Check | Concept evidence | Browser render evidence | Result |
|---|---|---|---|
| Information hierarchy | Quiet title/status header, preview dominant, controls/results on the right | Same two-column order at 1440px; semantic H1, preview region, complementary rail | Matched |
| Copy | Chinese control, empty-state, metadata, and footer labels | DOM snapshot contains every locked string with no added marketing copy | Matched |
| Palette | True white, charcoal preview, deep teal controls, restrained gray borders | CSS tokens use `#fff`, `#171a1b`, `#075f68`, and neutral border colors | Matched |
| Controls and icons | Full-width buttons with consistent rounded outline icons | Five action buttons share one component family and 1.75px SVG strokes | Matched |
| Container model | One working surface, no repeated dashboard card grid | One shell with an open preview column and control rail | Matched |
| Responsive layout | Compact product surface intended to stack on narrow screens | 390px check reports one grid column and no horizontal overflow | Matched |
| Status behavior | Clear idle/connected/error state area | Missing-agent interaction produces an explicit recovery message and re-enables Connect | Matched |

Above-the-fold copy diff: no unapproved eyebrow, badge, marketing claim, navigation, or fake metric was added. The initial status dot is gray rather than green because `未连接` is semantically offline; it turns green only after a successful connection.
