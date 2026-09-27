# Contributing

Start with the architecture and feature matrix. Keep changes scoped to the relevant library and add a regression that fails before the fix. Document any new file-format losses, memory limits or backend assumptions.

Use nullable C# and the repository's EditorConfig. UI controls belong in Controls/Editor/Workbench; pure model, kernels and formats must not depend on Uno. Editing operations must be transactional and leave valid document state after cancellation or failure. GPU acceleration must have an explicit unsupported/failure path, not a silently successful no-op.

Run both console validation projects and the browser acceptance suite for UI/rendering changes. Screenshot evidence complements, but never replaces, model and rendered-pixel assertions. Do not count a software-adapter run as physical-GPU performance validation.

Only contribute code and assets you have the right to license. No Adobe source, icons, fonts, sample files or reverse-engineered private service integrations. Public workspace conventions and documented file structures can inform independent implementations. Describe the actual compatibility boundary rather than labeling a feature 'full parity' without evidence.
