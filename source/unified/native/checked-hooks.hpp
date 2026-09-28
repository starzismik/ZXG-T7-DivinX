#pragma once
#include <cstddef>
namespace zxg {
// A failure at any stage must leave no installed hooks owned by this transaction.
// Backend methods return bool and are supplied by MinHook in production.
template<class Hooks, class Backend>
bool install_checked_hooks(const Hooks& hooks, Backend& backend) {
 std::size_t created=0;
 auto rollback=[&] {
  for(std::size_t i=0;i<created;++i)backend.disable(hooks[i]);
  for(std::size_t i=0;i<created;++i)backend.remove(hooks[i]);
 };
 for(const auto& hook:hooks){if(!backend.create(hook)){rollback();return false;}++created;}
 for(const auto& hook:hooks){if(!backend.queue(hook)){rollback();return false;}}
 if(!backend.apply()){rollback();return false;}
 return true;
}
}
