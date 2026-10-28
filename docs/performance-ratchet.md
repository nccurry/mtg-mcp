# Performance Baseline

> Historical reference: the old role-classification and Stats Lab performance
> checks were removed with their product implementations in Foundation Phase 2.
> Their benchmark tasks and reports are not present on this branch.

The repository has one current measurement: `task benchmark:oncurve` runs a
fixed 99-card Release benchmark of the `stats_simulate` engine, run as the `on-curve`
preset rules (the input the removed `local_deck_on_curve_estimate` was measured with). It is a
repeatable review baseline, not a hard CI gate.

The repository may add a performance measurement when an approved child adds a
production path with meaningful performance risk. Each measurement must name
the behavior it protects, use representative repeatable inputs, and say whether
it is a review guide or a hard CI gate.

Old benchmark code and report conventions remain in Git history as design
evidence. Do not reuse their product abstractions by default.
