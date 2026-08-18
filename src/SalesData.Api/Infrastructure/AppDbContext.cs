using Microsoft.EntityFrameworkCore;
using SalesData.Api.Domain;

namespace SalesData.Api.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<CleanProspect> CleanProspects => Set<CleanProspect>();
    public DbSet<BlockedProspect> BlockedProspects => Set<BlockedProspect>();
    public DbSet<CommonDomain> CommonDomains => Set<CommonDomain>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var customer = modelBuilder.Entity<Customer>();
        customer.ToTable("TBL_EXISTING_CUSTOMER");
        customer.HasKey(x => x.Id);
        customer.Property(x => x.Id).HasColumnName("ID");
        customer.Property(x => x.CustomerCode).HasColumnName("CUSTOMER_CODE").HasMaxLength(50);
        customer.Property(x => x.CompanyName).HasColumnName("COMPANY_NAME").HasMaxLength(100);
        customer.Property(x => x.CustomerEmail).HasColumnName("CUSTOMER_EMAIL").HasMaxLength(100);
        customer.Property(x => x.EmailDomain).HasColumnName("EMAIL_DOMAIN");
        customer.Property(x => x.ContactPerson).HasColumnName("CONTACT_PERSON").HasMaxLength(100);
        customer.Property(x => x.CustomerContactNumber1).HasColumnName("CUSTOMER_CONTACT_NUMBER1").HasMaxLength(30);
        customer.Property(x => x.CustomerContactNumber2).HasColumnName("CUSTOMER_CONTACT_NUMBER2").HasMaxLength(30);
        customer.Property(x => x.CustomerContactNumber3).HasColumnName("CUSTOMER_CONTACT_NUMBER3").HasMaxLength(30);
        customer.Property(x => x.CountryCode).HasColumnName("COUNTRY_CODE");
        customer.Property(x => x.Country).HasColumnName("COUNTRY").HasMaxLength(50);
        customer.Property(x => x.State).HasColumnName("STATE").HasMaxLength(50);
        customer.Property(x => x.City).HasColumnName("CITY").HasMaxLength(50);
        customer.Property(x => x.Category).HasColumnName("CATEGORY").HasMaxLength(50);
        customer.Property(x => x.CreatedBy).HasColumnName("CREATED_BY").HasMaxLength(100);
        customer.Property(x => x.CreatedOn).HasColumnName("CREATED_ON");
        customer.Property(x => x.ModifiedBy).HasColumnName("MODIFIED_BY").HasMaxLength(100);
        customer.Property(x => x.ModifiedOn).HasColumnName("MODIFIED_ON");
        customer.HasIndex(x => x.CustomerCode).IsUnique();
        customer.HasIndex(x => x.CustomerEmail).IsUnique();

        var country = modelBuilder.Entity<Country>();
        country.ToTable("Countries");
        country.Property(x => x.Id).HasColumnName("CountryId");
        country.Property(x => x.CountryName).HasColumnName("CountryName").HasMaxLength(100);
        country.Property(x => x.CountryCode).HasColumnName("CountryCode").HasMaxLength(10);

        ConfigureProspect(modelBuilder.Entity<CleanProspect>());
        ConfigureBlockedProspect(modelBuilder.Entity<BlockedProspect>());

        var commonDomain = modelBuilder.Entity<CommonDomain>();
        commonDomain.ToTable("TBL_COMMON_DOMAINS");
        commonDomain.Property(x => x.DomainName).HasColumnName("DomainName").HasMaxLength(255);
        commonDomain.HasIndex(x => x.DomainName).IsUnique();
    }

    private static void ConfigureProspect(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<CleanProspect> entity)
    {
        entity.ToTable("TBL_PROSPECT_CUSTOMER_CLEAN");
        entity.HasKey(x => x.Id); MapSharedProspectColumns(entity);
        entity.Property(x => x.ModifiedBy).HasColumnName("MODIFIED_BY").HasMaxLength(100);
        entity.Property(x => x.ModifiedOn).HasColumnName("MODIFIED_ON");
        entity.Property(x => x.SalesPersonId).HasColumnName("SALES_PERSON_ID");
        entity.HasIndex(x => x.CustomerEmail); entity.HasIndex(x => x.EmailDomain); entity.HasIndex(x => x.CompanyName);
        entity.HasIndex(x => x.CustomerContactNumber1); entity.HasIndex(x => new { x.CreatedBy, x.CreatedOn });
        entity.HasIndex(x => new { x.EventName, x.CreatedOn });
    }

    private static void ConfigureBlockedProspect(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<BlockedProspect> entity)
    {
        entity.ToTable("TBL_PROSPECT_CUSTOMER_BLOCKED");
        entity.HasKey(x => x.Id); MapSharedProspectColumns(entity);
        entity.Property(x => x.BlockedBy).HasColumnName("BLOCKED_BY").HasMaxLength(100);
        entity.Property(x => x.BlockReason).HasColumnName("BLOCK_REASON").HasMaxLength(300);
        entity.Property(x => x.Released).HasColumnName("RELEASED");
        entity.Property(x => x.ReleasedBy).HasColumnName("RELEASED_BY");
        entity.Property(x => x.ReleasedOn).HasColumnName("RELEASED_ON");
        entity.HasIndex(x => x.CustomerEmail); entity.HasIndex(x => x.CompanyName);
        entity.HasIndex(x => new { x.CreatedBy, x.CreatedOn }); entity.HasIndex(x => new { x.EventName, x.CreatedOn });
    }

    private static void MapSharedProspectColumns<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity) where T : class
    {
        entity.Property<int>(nameof(CleanProspect.Id)).HasColumnName("ID");
        entity.Property<string>(nameof(CleanProspect.CustomerCode)).HasColumnName("CUSTOMER_CODE").HasMaxLength(50);
        entity.Property<string>(nameof(CleanProspect.CompanyName)).HasColumnName("COMPANY_NAME").HasMaxLength(250);
        entity.Property<string>(nameof(CleanProspect.ContactPerson)).HasColumnName("CONTACT_PERSON").HasMaxLength(200);
        entity.Property<string?>(nameof(CleanProspect.CustomerContactNumber1)).HasColumnName("CUSTOMER_CONTACT_NUMBER1").HasMaxLength(30);
        entity.Property<string?>(nameof(CleanProspect.CustomerContactNumber2)).HasColumnName("CUSTOMER_CONTACT_NUMBER2").HasMaxLength(30);
        entity.Property<string?>(nameof(CleanProspect.CustomerContactNumber3)).HasColumnName("CUSTOMER_CONTACT_NUMBER3").HasMaxLength(30);
        entity.Property<string>(nameof(CleanProspect.CustomerEmail)).HasColumnName("CUSTOMER_EMAIL").HasMaxLength(320);
        entity.Property<string?>(nameof(CleanProspect.EmailDomain)).HasColumnName("EMAIL_DOMAIN").HasMaxLength(255);
        entity.Property<string?>(nameof(CleanProspect.CountryCode)).HasColumnName("COUNTRY_CODE").HasMaxLength(10);
        entity.Property<string?>(nameof(CleanProspect.Country)).HasColumnName("COUNTRY").HasMaxLength(100);
        entity.Property<string?>(nameof(CleanProspect.State)).HasColumnName("STATE").HasMaxLength(100);
        entity.Property<string?>(nameof(CleanProspect.City)).HasColumnName("CITY").HasMaxLength(100);
        entity.Property<string>(nameof(CleanProspect.Category)).HasColumnName("CATEGORY").HasMaxLength(50);
        entity.Property<string>(nameof(CleanProspect.CreatedBy)).HasColumnName("CREATED_BY").HasMaxLength(100);
        entity.Property<DateTime?>(nameof(CleanProspect.CreatedOn)).HasColumnName("CREATED_ON");
        entity.Property<string?>(nameof(CleanProspect.EventName)).HasColumnName("EVENT_NAME").HasMaxLength(200);
    }
}
