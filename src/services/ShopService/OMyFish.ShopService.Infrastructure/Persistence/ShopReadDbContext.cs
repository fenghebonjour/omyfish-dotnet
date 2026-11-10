using Microsoft.EntityFrameworkCore;

namespace OMyFish.ShopService.Infrastructure.Persistence;

/// Same model as ShopDbContext, connected to the Postgres read replica
/// (ConnectionStrings:Replica) — used only by ProductCatalogReader, never for writes.
public sealed class ShopReadDbContext(DbContextOptions<ShopReadDbContext> options) : ShopDbContext(options);
