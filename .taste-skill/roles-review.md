Scope: replace learning journey with the existing four-role introduction; match the existing green brand and type. No direction draw for this single-section change. No new pictures, mock data, or redraws.
Claim: each user can discover the functions available to their role.
Idea: the LMS's actual four account roles become an interactive function guide.
Live thing: switch between Học viên, Giảng viên, Trưởng khoa and Quản trị viên.

ROUND 1
Seen in one second: four role choices above the selected role and its functions.
Idea (one sentence): the LMS's actual four account roles become an interactive function guide.
Live thing: role selector with four real feature sets and a working login action.
Layout words by section: roles=selector + summary + feature list.
Scores (section only): design 7.5 · usability 8 · creativity 7 · content 8 → overall 7.6
Caps applied: none within the edited section; surrounding page is outside this small change.
Top three problems and fixes:
1. Mobile header navigation could clip its login action (roles-mobile-section first capture): allow navigation items to wrap.
2. Section heading inherited a bottom margin and misaligned its adjacent introduction: override it within the role introduction.
3. Old workflow styles and the #learning link would be dead after removal: remove them and point navigation to functions and library.

ROUND 2
Seen in one second: role title and a clearly highlighted selection.
Screens inspected: roles-after/desktop-page-01.jpg, roles-after/tablet-page-01.jpg, roles-mobile-section.jpg.
Mobile role controls fit a two-by-two grid; content has a consistent 24px edge. No remaining visible issue in the role section.
Validation: frontend production build passed; source design lint passed; browser interaction checks passed for all four roles at 1440, 768, 390 and 320px, including active state, feature count, removed workflow, valid anchors, no document overflow, and login opening. Desktop/tablet screenshots reviewed. The full inspector sweep and the desktop/tablet-only retry both stalled after saving screenshots and were stopped. No complete inspector report is claimed. Separate browser interaction checks and screenshots were used to verify the edited section.
