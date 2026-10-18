Scope: redesign the introduction, benefits, library and help sections shown in the user's screenshot; remove the four-role guide; add motion; auto fit to screen width; add password visibility and optional persistent login.
Direction: preserve the existing military-green palette and Be Vietnam Pro. Single-region redesign follows the established brand; no random direction draw or new imagery.
Claims: introduce the faculty's shared learning space / offer four real library categories / answer account and course access questions.
Idea: the faculty introduction and its learning benefits share one light/dark spread; library categories become a working reading index.
Live elements: library category buttons open login; native details disclose help; login supports password visibility and persistent/session storage selection.

ROUND 1
Seen in one second: a light introduction beside a dark green benefits panel.
Layout words: introduction=split colour field / library=index / help=heading plus disclosure list.
Scores for the edited region: design 7.5 · usability 8 · creativity 7 · content 8 → overall 7.6.
Top problems and fixes:
1. Original two sections repeated sparse two-column grids: use distinct colour field, index and disclosure compositions.
2. Fixed 1280px width wasted room on wide screens: use a shared fluid gutter and content width; clamp typography and spacing.
3. Dense mobile columns: stack introduction/benefits below 900px and library below 820px; wrap navigation.

ROUND 2
Reviewed screenshots: 1440-about.jpg, 2560-about.jpg, 390-about.jpg, login-mobile.jpg.
No visible clipping or unfilled gap caused by the removed role guide. Mobile headings and paragraphs remain readable; the login form fits at 390px.
Checks: frontend production build passed. Source design lint passed. Real Chrome interaction checks passed at 320, 390, 768, 1440, 1920 and 2560px: fluid width against header gutter, no horizontal overflow, all reveal elements observed and visible, distinct animation styles, valid navigation, four library login actions, help disclosure, login and reduced motion. Content remains visible with IntersectionObserver unavailable.
Auth checks used isolated-browser fake API responses, without real accounts: password show/hide preserves value and does not submit; remember=false stores only the tab session; remember=true persists login; token renewal retains the selected storage mode; a fresh tab sees only persistent sessions; logout clears both stores; password is never stored. Password-manager autocomplete uses username/current-password.
The bundled page-wide inspector had stalled in prior runs; current results are from explicit Chrome checks and screenshots, not a completed inspector report.
Assets: existing hero-students.png and school emblem only. No stock imagery, sample production records, new image credits or redraws.
