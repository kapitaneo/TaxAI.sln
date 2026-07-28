using System.Runtime.CompilerServices;

// The DbContext, the entity configurations, and the port implementations are
// internal: the only supported way into this module is the registration
// extension. The test project inspects those seams without widening the
// production surface.
[assembly: InternalsVisibleTo("Persistence.Tests")]
