# Performance Ratchet

> Historical reference: the legacy role-classification and Stats Lab timing
> ratchets were removed with their product implementations in Foundation Phase
> 2. Their benchmark tasks and report artifact are not present on this branch.

The repository may add performance measurements when an approved child
introduces a production path with a meaningful performance risk. New ratchets
must name the behavior they protect, use representative deterministic inputs,
and distinguish informational review budgets from hard CI gates.

Legacy benchmark code and report conventions remain available in Git history
as design evidence; they are not reusable product abstractions by default.
