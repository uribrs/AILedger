# Move TenableIo batch storage flag to configuration

Add a default-false `BatchScopedStorage` property to TenableIo collector configuration and wire the correlated findings emitter to that property instead of a hard-coded boolean.
