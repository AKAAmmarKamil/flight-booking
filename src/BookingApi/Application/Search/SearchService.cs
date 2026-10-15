using BookingApi.Application.Common;
using BookingApi.Application.Pricing;
using BookingApi.Application.Suppliers;
using BookingApi.Domain;
using BookingApi.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace BookingApi.Application.Search;

public class SearchService : ISearchService
{
    private static readonly TimeSpan OfferLifetime = TimeSpan.FromMinutes(30);

    private readonly IEnumerable<IFlightSupplier> _suppliers;
    private readonly PriceCalculator _priceCalculator;
    private readonly SearchOptions _options;
    private readonly BookingDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        IEnumerable<IFlightSupplier> suppliers,
        PriceCalculator priceCalculator,
        IOptions<SearchOptions> options,
        BookingDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<SearchService> logger)
    {
        _suppliers = suppliers;
        _priceCalculator = priceCalculator;
        _options = options.Value;
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ServiceResponse<SearchResultDto>> SearchAsync(SearchForm form, CancellationToken cancellationToken)
    {
        SearchRequest request = new(
            From: form.From,
            To: form.To,
            Date: form.Date,
            Adults: form.Adults);

        using var searchTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        searchTimeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var outcomes = await Task.WhenAll(
            _suppliers.Select(supplier => SearchOneSupplierAsync(supplier, request, searchTimeout.Token, cancellationToken)));

        var uniqueOffers = OfferDeduplicator.DeduplicateAndSort(outcomes.SelectMany(outcome => outcome.Offers));

        // The request token, not searchTimeout.Token: that one is already cancelled whenever a supplier timed out.
        var savedOffers = await SaveOffersAsync(uniqueOffers, request.Adults, cancellationToken);

        return new ServiceResponse<SearchResultDto>(new SearchResultDto
        {
            Offers = savedOffers
                .Select(ToOfferDto)
                .ToList(),
            Suppliers = outcomes
                .Select(outcome => new SupplierSearchResultDto
                {
                    Name = outcome.SupplierName,
                    Status = outcome.Status,
                    Count = outcome.Offers.Count
                })
                .ToList()
        });
    }

    private async Task<SupplierSearchOutcome> SearchOneSupplierAsync(
        IFlightSupplier supplier, SearchRequest request, CancellationToken searchToken, CancellationToken requestToken)
    {
        try
        {
            var offers = await supplier.SearchAsync(request, searchToken);

            var pricedOffers = offers
                .Select(offer => new PricedOffer(offer, _priceCalculator.ToIqd(offer.OriginalPrice, offer.OriginalCurrency)))
                .ToList();

            return new SupplierSearchOutcome(supplier.Name, SupplierSearchStatus.Ok, pricedOffers);
        }
        catch (OperationCanceledException) when (searchToken.IsCancellationRequested && !requestToken.IsCancellationRequested)
        {
            _logger.LogWarning("{Supplier} search did not answer within {TimeoutSeconds} s.", supplier.Name, _options.TimeoutSeconds);
            return new SupplierSearchOutcome(supplier.Name, SupplierSearchStatus.TimedOut, []);
        }
        catch (Exception exception) when (!requestToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "{Supplier} search failed.", supplier.Name);
            return new SupplierSearchOutcome(supplier.Name, SupplierSearchStatus.Failed, []);
        }
    }

    private async Task<List<Offer>> SaveOffersAsync(IEnumerable<PricedOffer> pricedOffers, int adults, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var offerIdPrefixes = _suppliers.ToDictionary(supplier => supplier.Name, supplier => supplier.OfferIdPrefix);

        var offers = pricedOffers
            .Select(pricedOffer => new Offer
            {
                Id = $"{offerIdPrefixes[pricedOffer.Offer.Supplier]}-{Guid.NewGuid():N}",
                Supplier = pricedOffer.Offer.Supplier,
                SupplierReference = pricedOffer.Offer.SupplierReference,
                Airline = pricedOffer.Offer.Airline,
                FlightNumber = pricedOffer.Offer.FlightNumber,
                From = pricedOffer.Offer.From,
                To = pricedOffer.Offer.To,
                Departure = pricedOffer.Offer.Departure,
                Arrival = pricedOffer.Offer.Arrival,
                Stops = pricedOffer.Offer.Stops,
                OriginalPrice = pricedOffer.Offer.OriginalPrice,
                OriginalCurrency = pricedOffer.Offer.OriginalCurrency,
                PriceIqd = pricedOffer.PriceIqd,
                Adults = adults,
                CreatedAt = now,
                ExpiresAt = now + OfferLifetime
            })
            .ToList();

        _dbContext.Offers.AddRange(offers);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return offers;
    }

    private static OfferDto ToOfferDto(Offer offer) =>
        new()
        {
            OfferId = offer.Id,
            Supplier = offer.Supplier,
            Airline = offer.Airline,
            FlightNumber = offer.FlightNumber,
            From = offer.From,
            To = offer.To,
            Departure = offer.Departure,
            Arrival = offer.Arrival,
            Stops = offer.Stops,
            PriceIqd = offer.PriceIqd,
            OriginalPrice = offer.OriginalPrice,
            OriginalCurrency = offer.OriginalCurrency
        };
}
