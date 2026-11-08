#pragma once
#include <stdexcept>

namespace setup {
enum class OperationPhase { Ready, Running, Cancelling, Succeeded, Failed, Cancelled };
struct OperationState {
    OperationPhase phase = OperationPhase::Ready;
    bool Pending() const { return phase == OperationPhase::Running || phase == OperationPhase::Cancelling; }
    bool Terminal() const { return phase == OperationPhase::Succeeded || phase == OperationPhase::Failed || phase == OperationPhase::Cancelled; }
    void Begin() {
        if (phase != OperationPhase::Ready) throw std::logic_error("OPERATION_ALREADY_STARTED");
        phase = OperationPhase::Running;
    }
    void Cancel() { if (phase == OperationPhase::Running) phase = OperationPhase::Cancelling; }
    void Finish(bool cancelled, bool succeeded) {
        if (!Pending()) throw std::logic_error("OPERATION_NOT_RUNNING");
        phase = cancelled ? OperationPhase::Cancelled : succeeded ? OperationPhase::Succeeded : OperationPhase::Failed;
    }
};
}
