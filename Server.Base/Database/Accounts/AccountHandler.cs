﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Base.Accounts.Enums;
using Server.Base.Accounts.Extensions;
using Server.Base.Accounts.Helpers;
using Server.Base.Core.Configs;
using Server.Base.Core.Services;
using Server.Base.Logging;
using Server.Base.Network;
using Server.Base.Network.Helpers;
using System.Net;

namespace Server.Base.Database.Accounts;

public class AccountHandler(PasswordHasher hasher, AccountAttackLimiter attackLimiter, IpLimiter ipLimiter, BaseLock bLock,
    FileLogger fileLogger, TemporaryDataStorage temporaryDataStorage, InternalRConfig config, IServiceProvider services) :
    DataHandler<AccountDbEntry, BaseDatabase, BaseLock>(services, bLock)
{
    public override bool HasDefault => true;

    public override AccountDbEntry CreateDefault()
    {
        Logger.LogInformation("Username: ");
        var username = Console.ReadLine();

        Logger.LogInformation("Password: ");
        var password = Console.ReadLine();

        Logger.LogInformation("Email: ");
        var email = Console.ReadLine();

        if (username != null)
            return new AccountDbEntry(username, password, email, hasher)
            {
                AccessLevel = AccessLevel.Owner
            };

        Logger.LogError("Username for account is null!");
        return null;
    }

    public AccountModel GetAccountFromId(int id) =>
        GetAccountFromModel(Get(id));

    public AccountModel GetAccountFromUsername(string username) =>
        GetAccountFromId(GetIdFromUserName(username));

    public static AccountModel GetAccountFromModel(AccountDbEntry model) =>
        model != null ? new(model) : null;

    protected int GetIdFromUserName(string username)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BaseDatabase>();

        lock (DbLock.Lock)
        {
            var account = db.Accounts.AsNoTracking().FirstOrDefault(a => a.Username == username);

            return account == null ? -1 : account.Id;
        }
    }

    public AlrReason GetAccount(string username, string password, NetState netState)
    {
        var rejectReason = AlrReason.Invalid;

        if (!config.SocketBlock && !ipLimiter.Verify(netState.Address))
        {
            IpLimitedError(netState);
            rejectReason = AlrReason.InUse;
        }
        else
        {
            AccountModel account;

            if (username == ".")
            {
                account = new AccountModel(temporaryDataStorage.GetData<AccountDbEntry>(password));

                if (account == null)
                    rejectReason = AlrReason.BadComm;
                else
                    username = account.Username;
            }
            else
            {
                account = GetAccountFromUsername(username);

                if (account != null)
                    if (!hasher.CheckPassword(account, password))
                        rejectReason = AlrReason.BadPass;
            }

            if (account != null)
                if (!account.HasAccess(netState, config))
                    rejectReason = config.LockDownLevel > AccessLevel.Vip
                        ? AlrReason.BadComm
                        : AlrReason.BadPass;
                else if (account.IsBanned())
                    rejectReason = AlrReason.Blocked;
                else if (rejectReason is not AlrReason.BadPass and not AlrReason.BadComm)
                {
                    netState.Set(account);
                    rejectReason = AlrReason.Accepted;
                }
        }

        var errorReason = rejectReason switch
        {
            AlrReason.Accepted => "Valid credentials",
            AlrReason.BadComm => "Access denied",
            AlrReason.BadPass => "Invalid password",
            AlrReason.Blocked => "Banned account",
            AlrReason.InUse => "Past IP limit threshold",
            AlrReason.Invalid => "Invalid username",
            _ => throw new ArgumentOutOfRangeException(rejectReason.ToString())
        };

        fileLogger.WriteGenericLog<AccountHandler>("login", $"Login: {netState}", $"{errorReason} for '{username}'",
            rejectReason == AlrReason.Accepted ? LoggerType.Debug : LoggerType.Error);

        if (rejectReason is not AlrReason.Accepted and not AlrReason.InUse)
            attackLimiter.RegisterInvalidAccess(netState);

        return rejectReason;
    }

    public AccountModel Create(IPAddress ipAddress, string username, string password, string email)
    {
        if (username.Trim().Length <= 0 || password.Trim().Length <= 0 || email.Trim().Length <= 0)
        {
            Logger.LogInformation("Login: {Address}: User post _data for '{Username}' is invalid in length!",
                ipAddress, username);
            return null;
        }

        var isSafe = !(username.StartsWith(' ') || username.EndsWith(' ') || username.EndsWith('.'));

        for (var i = 0; isSafe && i < username.Length; ++i)
            isSafe = username[i] >= 0x20 && username[i] < 0x7F &&
                     config.ForbiddenChars.All(t => username[i] != t);

        for (var i = 0; isSafe && i < password.Length; ++i)
            isSafe = password[i] is >= (char)0x20 and < (char)0x7F;

        if (!isSafe)
        {
            Logger.LogInformation("Login: {Address}: User password for '{Username}' is unsafe! Returning...",
                ipAddress, username);
            return null;
        }

        Logger.LogInformation("Login: {Address}: Creating new account '{Username}'",
            ipAddress, username);

        var account = new AccountDbEntry(username, password, email, hasher);

        Add(account);

        return GetAccountFromModel(account);
    }

    public void IpLimitedError(NetState netState) =>
        fileLogger.WriteGenericLog<IpLimiter>("ipLimits", netState.ToString(), "Past IP limit threshold", LoggerType.Debug);

    public bool ContainsUsername(string username)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BaseDatabase>();

        lock (DbLock.Lock)
        {
            return db.Accounts.Any(a => a.Username == username);
        }
    }

    public bool ContainsEmail(string email)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BaseDatabase>();

        lock (DbLock.Lock)
        {
            return db.Accounts.Any(a => a.Email == email);
        }
    }
}
