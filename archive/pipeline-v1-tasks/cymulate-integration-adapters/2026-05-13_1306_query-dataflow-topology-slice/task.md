# Query Dataflow Topology Slice

Create the next bounded Shared Query infrastructure slice by adding the smallest executable Query Dataflow topology composition that wires existing SDK query pipeline stage contracts after the completed plan builder, optimizer, and native coalescer stages.

This slice does not implement vendor clients, concrete dispatcher/distributor/matcher/tracker behavior, recovery persistence, or unresolved product policy.
