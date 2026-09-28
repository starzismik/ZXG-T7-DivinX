#pragma once
#include <cstdint>
namespace divinium {
using LiquidAmount=std::int64_t;
enum class ProviderResult { Pending, Completed, Failed };
// No game access in this interface. Completion must always be verified by a fresh read.
struct IncreaseProvider {
 virtual ~IncreaseProvider()=default;
 virtual void IncreaseLiquid(LiquidAmount delta)=0;
 virtual ProviderResult Poll()=0;
 virtual void Cancel()=0;
};
struct UnavailableIncreaseProvider final : IncreaseProvider {
 void IncreaseLiquid(LiquidAmount) override {}
 ProviderResult Poll() override {return ProviderResult::Failed;}
 void Cancel() override {}
};
}
