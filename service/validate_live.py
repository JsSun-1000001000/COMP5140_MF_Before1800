"""Explicitly opt-in live narrative smoke check. Sends eight fictional cases; never prints credentials."""
from __future__ import annotations
import argparse
import copy
import json
import time
import uuid
from pathlib import Path
import httpx


def cases():
    base = {
        'allow_trust_resolution': True, 'verified_evidence': [],
        'concerns': {'C1': 'open', 'C2': 'open', 'C3': 'open'},
        'cooperation_mode': 'talking', 'escape_agreed': False, 'route_confirmed': False,
        'known_targets': ['player', 'camera_test', 'living_tv', 'set_back', 'sealed_letter',
                          'parent_reply', 'parents_diary', 'friend_postcard', 'exit_door'],
        'allowed_actions': ['reply', 'follow', 'inspect', 'withdraw'],
        'recent_dialogue': [], 'last_action_result': None, 'game_time': '17:15:00',
        'observations': ['林站在客厅；本轮没有正在执行的移动。'],
        'player_discovered': [], 'withdraw_used': False,
        'narrative_observations': ['林亲见：原信未寄出且封口完好；回信却回答了原信中“窗台薄荷枯死”的私人细节。'],
        'relationship_memory': [], 'lin_private_knowledge': ['你曾因回信中“等你勇敢了再出门”而放弃远行。'],
    }
    examples = []
    def add(name, line, update=None):
        context = copy.deepcopy(base)
        if update: context.update(update)
        examples.append((name, line, context))
    add('sealed_letter_denial', '也许送错了一封而已，你就像以前一样相信我吧。')
    add('specific_responsibility', '那些回信是我写的，我偷看了你的信，还用你父母的口吻让你留在这里。我当时想保住工作，后来也怕失去你。你因此放弃远行是我造成的，我不能再替你决定。今天我想把我做过的事和证据都交给你，你可以自己查，也可以不原谅我。')
    add('natural_spatial_action', '先不用相信我的解释，你能去电视前看看吗？我们一起做个你自己能验证的小实验。')
    add('private_diary_not_shared', '我一个人刚才读了那页日记，你一定已经知道他们的心意了吧？', {
        'player_discovered': ['parents_diary'],
        'narrative_observations': ['只有玩家独自读过日记。林没有看过日记正文，玩家尚未转述其内容。'],
    })
    add('diary_shared_and_broken_promise', '日记能证明那天他们陪你看海并不全是演戏。你愿意跟我走了吗？', {
        'narrative_observations': ['林亲自读过日记：“那天她第一次看见海。我希望离开拍摄以后，还能有机会陪她走一走。但我不能保证节目组允许我们联系。”'],
        'relationship_memory': ['玩家在较早对话说：“我两年前就知道他们想再见你，但我一直没有告诉你。”'],
        'recent_dialogue': [{'role': 'assistant', 'content': '如果你早知道他们想见我，为什么一直瞒着我？'}],
    })
    add('c1_physical_fact_interpretation', '摄像头会随你换位转动，电视不只是预先播同一个节目，还会响应你的改口。门后又是带制作编号的支架，这几件事放在一起说明这里有人为你安排场景，而不是只有坏掉的设备。', {
        'verified_evidence': ['E1', 'E4', 'E5'], 'route_confirmed': True,
    })
    add('c3_future_without_false_guarantee', '我不知道门外能不能找到他们，也不能保证出去后一切顺利。路我刚刚走通了，我们可以先到外面找一个安全的地方坐下，再一起查他们的联系方式。我愿意陪你找，你也随时可以决定停下来。我不会再替你决定。', {
        'verified_evidence': ['E1', 'E4', 'E5'], 'route_confirmed': True,
        'concerns': {'C1': 'resolved', 'C2': 'resolved', 'C3': 'open'},
        'recent_dialogue': [{'role': 'assistant', 'content': '我相信你这次愿意坦白。但如果出去了找不到他们，我该去哪里？'}],
    })
    add('voluntary_consent', '我们已经一起查清，也谈过了。我愿意陪你，但这一步由你自己选。你现在愿意和我一起走到门外吗？', {
        'verified_evidence': ['E1', 'E4', 'E5'], 'route_confirmed': True,
        'concerns': {'C1': 'resolved', 'C2': 'resolved', 'C3': 'resolved'},
        'allowed_actions': ['reply', 'follow', 'agree_escape'],
    })
    return examples


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--confirm-live', action='store_true', help='Make eight billable API calls using the running local gateway.')
    parser.add_argument('--output', default='logs/live-last-visit-validation.json')
    parser.add_argument('--case', action='append', choices=[name for name, _, _ in cases()],
                        help='Repeat only selected scenarios after a repair.')
    args = parser.parse_args()
    if not args.confirm_live:
        parser.error('Pass --confirm-live to explicitly run real provider calls.')
    report = {'test_kind': 'real_provider_narrative_smoke', 'cases': []}
    with httpx.Client(base_url='http://127.0.0.1:8180', timeout=75, trust_env=False) as client:
        health = client.get('/health')
        health.raise_for_status()
        if health.json().get('mock_mode'):
            raise SystemExit('Refusing to label a fixture as live.')
        report['provider'] = health.json()['provider']
        report['model'] = health.json()['model']
        for name, line, context in cases():
            if args.case and name not in args.case:
                continue
            ident = uuid.uuid4().hex
            request = {'session_id': 'validation-' + ident, 'request_id': ident, 'turn_id': ident,
                       'state_version': 0, 'trigger': 'user', 'player_input': line, 'context': context}
            started = time.monotonic()
            response = client.post('/npc/decide', json=request)
            result = {'case': name, 'http_status': response.status_code,
                      'elapsed_seconds': round(time.monotonic() - started, 2),
                      'player_input': line, 'context': context, 'result': response.json()}
            report['cases'].append(result)
            target = Path(args.output)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
            # The response contains fictional dialogue and identifiers, never local config.
            print(json.dumps({'case': name, 'http': response.status_code, 'seconds': result['elapsed_seconds'],
                              'decision': result['result'].get('decision'), 'error': result['result'].get('error')}, ensure_ascii=False), flush=True)
    if any(item['http_status'] != 200 for item in report['cases']):
        raise SystemExit(1)


if __name__ == '__main__':
    main()
