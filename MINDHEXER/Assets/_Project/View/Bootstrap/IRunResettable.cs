namespace Game.View
{
    /// <summary>
    /// 사망 후 재시작(<see cref="Main.RestartRun"/>) 시 "판을 처음 상태로" 되돌려야 하는 컴포넌트가 구현한다.
    ///
    /// ★ 왜 인터페이스인가: 예전엔 Main이 리셋할 컴포넌트를 손으로 나열해서, 상태를 가진 컴포넌트를
    ///   새로 추가할 때마다 목록에 넣는 걸 잊으면 조용히 재시작이 안 됐다. 이제 이걸 구현하기만 하면
    ///   Main을 건드리지 않아도 자동으로 재시작에 참여한다(누락 불가).
    ///
    /// 씬은 다시 로드하지 않으므로(부트스트랩이 세션당 1회라 리로드하면 사라짐) 제자리에서 리셋한다.
    /// </summary>
    public interface IRunResettable
    {
        /// <summary>이 컴포넌트를 판 시작 상태로 되돌린다. 연출 없이 즉시.</summary>
        void ResetForRestart();
    }
}
