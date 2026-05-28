using AutoMapper;
using Finance.Accounts.API.Interfaces;
using Finance.Accounts.DBModel.Models;
using Finance.ServiceModel.Accounts;

namespace Finance.Accounts.API.Services;

/// <summary>
/// Default <see cref="IAccountService"/> implementation.
/// </summary>
public sealed class AccountService : IAccountService
{
    private readonly IAccountRepository _accounts;
    private readonly IMapper _mapper;

    /// <summary>Creates a new <see cref="AccountService"/>.</summary>
    public AccountService(IAccountRepository accounts, IMapper mapper)
    {
        _accounts = accounts;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountDto>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Account> entities = await _accounts.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return _mapper.Map<IReadOnlyList<AccountDto>>(entities);
    }

    /// <inheritdoc />
    public async Task<AccountDto?> GetAsync(int id, CancellationToken cancellationToken)
    {
        Account? entity = await _accounts.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return entity is null ? null : _mapper.Map<AccountDto>(entity);
    }

    /// <inheritdoc />
    public async Task<AccountDto> CreateAsync(
        CreateAccountRequest request,
        string countryCode,
        CancellationToken cancellationToken)
    {
        Account entity = new()
        {
            Code = request.Code,
            Name = request.Name,
            Type = request.Type,
            ParentId = request.ParentId,
            CountryCode = countryCode,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Account saved = await _accounts.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        return _mapper.Map<AccountDto>(saved);
    }

    /// <inheritdoc />
    public async Task<AccountDto?> UpdateAsync(
        int id,
        UpdateAccountRequest request,
        CancellationToken cancellationToken)
    {
        Account? entity = await _accounts.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        entity.Name = request.Name;
        entity.IsActive = request.IsActive;

        await _accounts.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
        return _mapper.Map<AccountDto>(entity);
    }
}
