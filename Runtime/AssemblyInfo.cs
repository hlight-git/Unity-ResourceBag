using System.Runtime.CompilerServices;

// The clock's offline-grant arithmetic is the one piece of this package that cannot be
// exercised through the public API: it depends on wall-clock and monotonic readings that a
// test cannot move. Exposing it as internal and testing it directly beats either widening
// the public surface or leaving it unverified — a bug in it silently pays players twice.
[assembly: InternalsVisibleTo("Hlight.ResourceBag.Tests")]
