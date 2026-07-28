using System.Runtime.CompilerServices;

// The persisted read-model constructors are internal: only Tenancy's own
// fail-closed projection may create a persisted record, so a caller cannot
// assemble one that skipped the guards. The test project inspects that seam
// without widening the production surface.
//
// The grant is to this module's own test project and to nothing else. In
// particular Persistence is NOT granted access: EF Core materializes non-public
// members by reflection and needs no grant, and handing the storage layer a
// construction path around the projection is exactly what this seam prevents.
[assembly: InternalsVisibleTo("Tenancy.Tests")]
