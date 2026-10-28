# Midnight Concierge — Phase 6 & 7 refinement

Authority: 27 October 2026 brief, attachment 61389ead-b1a7-427e-948a-e77181ff5e14. Local presentation only; no production package, deployment, controller, policy, financial, schema, private storage or Identity changes.

## Audit and preserved interactions
The focused Phase 6 shell and Client Hub currently override the public midnight palette with off-white surfaces and dark text in booking-concierge.css and client-hub.css. Marketing/authentication styles and source remain protected. Preserve all four wizard panels/IDs, availability checks, native package radios, dynamic catalog/prices/inclusions, max-two removals/single discount, one moved pricing subtree, native date fallback and keyboard calendar, private receipt flows, actual next-action helper, chat/polling and account/feedback contracts.

## Plan
1. Replace the local light palette with shared --mc-* semantic transactional tokens; retain distinct page, surface, elevated/estimate, input, state and text roles. Scope to client-concierge; no public/internal theme changes. Refine sizing, readable small text, section hierarchy and mobile reflow.
2. Retain the wizard architecture. Add one proportionally framed, approved performance context image beside the location form and a compact neutral image panel above package selection. No per-package photograph without authoritative matching inclusions; technical fixture rows cannot establish an association. Preserve original images/watermarks and reuse existing WebP variants.
3. Replace primary terms details with a permanently visible six-clause section, reusing the current complete _ConciergeTerms wording verbatim in a shared text partial. Add an accessible full-text dialog/link, a visible unchecked required agreement and preserve existing server boolean validation. No new legal wording/version/timestamp claim. Historical CreateStepTwo wording remains unchanged.
4. Group Client navigation into Your space, Event management, Account & activity, and secondary actions. Keep refund history, all original routes, badges and role boundaries. Exactly one active link per visible surface. Retain native modal/no-script navigation; lock background scroll with restoration and test backdrop/Escape/focus.
5. Apply the tokens coherently to all Client payments/refunds, rescheduling/history, detail/list, messages, notifications, profile/dialogs and feedback. Make Overview next-action surface dominant, flatten secondary activity emphasis.

## Checkpoint and QA
Verified pre-edit 554-file source checkpoint identified by .tmp-owner-build/midnight-checkpoint.json; exclude user index.html/package-lock.json and all ignored secrets/private data. Preserve old 5295/5298 preview processes and their databases. Use a new independent loopback 5299 GUID LocalDB fixture for current-source manual review and an independent 5297 transaction fixture for mutating browser flows.

Render 320/375/390/430/768/1024/1440px. Capture desktop/mobile wizard location, selection, details, review, visible terms/full terms, payment upload, rescheduling, confirmation, Overview/list/payment/messages/notifications/profile/feedback, sidebar/drawer. Compare real previous captures with explicit fixture-state caveats. Test contrast, forms, keyboard radios/calendar, agreement requirement/full-text focus, drawer background scroll/close/focus, image proportions, business/security regressions, cross-public/internal isolation. Run Debug/Release, Owner/SQL integration, DOM, browser and scoped axe. Zero automated findings is not full WCAG certification. Keep all previous H4/H5/catalog/manual/provider limitations open.

Stop after local Phase 6 & 7 refinement for visual approval.
