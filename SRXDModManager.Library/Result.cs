using System;

namespace SRXDModManager.Library;

public readonly struct Result {
    private readonly bool isSuccess;
    private readonly string failureMessage;

    internal Result(bool isSuccess, string failureMessage) {
        this.isSuccess = isSuccess;
        this.failureMessage = failureMessage;
    }

    public bool TryGetValue(out string failureMessage) {
        failureMessage = this.failureMessage;

        return isSuccess;
    }

    public Result Then(Func<Result> next) => isSuccess ? next() : new Result(false, failureMessage);

    public Result<TNext> Then<TNext>(Func<Result<TNext>> next) => isSuccess ? next() : new Result<TNext>(default, false, failureMessage);

    public static Result Success() => new(true, string.Empty);

    public static Result Failure(string message) => new(false, message);
}

public readonly struct Result<T> {
    private readonly T value;
    private readonly bool isSuccess;
    private readonly string failureMessage;

    internal Result(T value, bool isSuccess, string failureMessage) {
        this.value = value;
        this.isSuccess = isSuccess;
        this.failureMessage = failureMessage;
    }

    public bool TryGetValue(out T value, out string failureMessage) {
        value = this.value;
        failureMessage = this.failureMessage;

        return isSuccess;
    }

    public Result Then(Func<T, Result> next) => isSuccess ? next(value) : new Result(false, failureMessage);
    
    public Result<TNext> Then<TNext>(Func<T, Result<TNext>> next) => isSuccess ? next(value) : new Result<TNext>(default, false, failureMessage);
    
    public static Result<T> Success(T value) => new(value, true, string.Empty);

    public static Result<T> Failure(string message) => new(default, false, message);
}